using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public sealed class CustomerNotFoundException(string message) : Exception(message) { }
public sealed class InvalidCouponException(string message) : Exception(message) { }
public sealed class DatabaseWriteException(string message) : Exception(message) { }
public sealed class SmtpUnavailableException(string message) : Exception(message) { }

public sealed class CustomerService(CustomerRepository customers)
{
    public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct = default)
    {
        Customer? customer = await customers.GetByIdAsync(id, ct);
        return customer ?? throw new CustomerNotFoundException($"Customer #{id} not found");
    }
}

public sealed class InventoryService(ProductRepository products)
{
    public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct = default)
    {
        var reserved = new List<OrderItem>();
        try
        {
            foreach (OrderItem item in order.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (await products.ReserveStockAsync(item.ProductId, item.Quantity, ct))
                {
                    reserved.Add(item);
                    continue;
                }

                foreach (OrderItem prior in reserved)
                    await products.ReleaseStockAsync(prior.ProductId, prior.Quantity, CancellationToken.None);

                Product? product = await products.GetByIdAsync(item.ProductId, CancellationToken.None);
                order.FailureReason = $"Insufficient stock for '{product?.Name ?? $"Product #{item.ProductId}"}'";
                return false;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            foreach (OrderItem prior in reserved)
                await products.ReleaseStockAsync(prior.ProductId, prior.Quantity, CancellationToken.None);
            throw;
        }
    }

    public async Task ReleaseStockAsync(Order order, CancellationToken ct = default)
    {
        foreach (OrderItem item in order.Items)
            await products.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }
}

public sealed class PricingService(
    JsonFileStore store,
    CustomerRepository customers,
    CouponRepository coupons)
{
    public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct = default)
    {
        await SimulatedLatency.DatabaseQueryAsync("PricingService", $"Pricing Order #{order.Id}", ct);
        List<Product> products = await store.ReadAllAsync<Product>(DataFiles.Products, ct);
        decimal subtotal = 0m;
        decimal weight = 0m;
        int quantity = 0;

        foreach (OrderItem item in order.Items)
        {
            Product? product = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product is null) throw new InvalidOperationException($"Product #{item.ProductId} not found");
            item.UnitPrice = product.Price;
            subtotal += product.Price * item.Quantity;
            weight += product.WeightKg * item.Quantity;
            quantity += item.Quantity;
        }
        order.SubTotal = Money(subtotal);

        Task<decimal> discountTask = GetDiscountAsync(order, ct);
        Task<decimal> shippingTask = GetShippingAsync(order, quantity, weight, ct);
        // Await both started tasks even if the discount task fails, so no fault is left unobserved.
        order.ShippingCost = await shippingTask;
        order.Discount = await discountTask;
        order.TotalAmount = Money(order.SubTotal - order.Discount + order.ShippingCost);
        return order.TotalAmount;
    }

    private async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct)
    {
        await SimulatedLatency.ExternalApiAsync("PricingService", $"Discount Order #{order.Id}", ct);
        Customer? customer = await customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is null) throw new CustomerNotFoundException($"Customer #{order.CustomerId} not found");

        int loyalty = customer.LoyaltyTier switch
        {
            LoyaltyTier.Bronze => 0,
            LoyaltyTier.Silver => 5,
            LoyaltyTier.Gold => 10,
            _ => 0
        };
        int couponDiscount = 0;
        if (order.CouponCode is not null)
        {
            Coupon? coupon = await coupons.GetByCodeAsync(order.CouponCode, ct);
            if (coupon is null) throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");
            if (coupon.ExpiryDate.Date < DateTime.Today)
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
            couponDiscount = coupon.DiscountPercent;
        }
        return Money(order.SubTotal * Math.Min(30, loyalty + couponDiscount) / 100m);
    }

    private static async Task<decimal> GetShippingAsync(Order order, int quantity, decimal weight, CancellationToken ct)
    {
        await SimulatedLatency.ExternalApiAsync("PricingService", $"Shipping Order #{order.Id}", ct);
        return Money(order.SubTotal >= 1000m ? 0m : 5m + 1.50m * quantity + 2m * weight);
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public interface IPaymentGateway
{
    Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct = default);
    Task RefundAsync(string transactionId, CancellationToken ct = default);
}

public sealed class PaymentGateway(JsonFileStore store, IEnumerable<int>? timeoutOrderIds = null,
    IEnumerable<int>? declineOrderIds = null) : IPaymentGateway
{
    private readonly HashSet<int> _timeouts = new(timeoutOrderIds ?? []);
    private readonly HashSet<int> _declines = new(declineOrderIds ?? []);

    public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct = default)
    {
        Log.Info("PaymentGateway", $"Order #{orderId}: Authorizing payment...");
        await Task.Delay(800, ct);
        Log.Info("PaymentGateway", $"Order #{orderId}: Fraud check...");
        await Task.Delay(500, ct);

        if (_declines.Contains(orderId))
        {
            var declined = new PaymentResult { Success = false, Status = PaymentStatus.Declined, Message = "Card declined by issuer" };
            await AppendPaymentAsync(new PaymentTransaction
            {
                OrderId = orderId, Amount = amount, Status = PaymentStatus.Declined,
                TransactionId = null, CreatedAt = DateTime.Now
            }, ct);
            return declined;
        }

        Log.Info("PaymentGateway", $"Order #{orderId}: Capturing payment...");
        await Task.Delay(_timeouts.Contains(orderId) ? 5000 : 700, ct);
        string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        await AppendPaymentAsync(new PaymentTransaction
        {
            OrderId = orderId, Amount = amount, Status = PaymentStatus.Captured,
            TransactionId = transactionId, CreatedAt = DateTime.Now
        }, ct);
        return new PaymentResult { Success = true, Status = PaymentStatus.Captured, TransactionId = transactionId };
    }

    public async Task RefundAsync(string transactionId, CancellationToken ct = default)
    {
        await SimulatedLatency.ExternalApiAsync("PaymentGateway", $"Refund {transactionId}", ct);
        await store.UpdateAsync<PaymentTransaction>(DataFiles.Payments, records =>
        {
            PaymentTransaction? payment = records.FirstOrDefault(p => p.TransactionId == transactionId);
            if (payment is null) throw new InvalidOperationException($"Payment {transactionId} not found");
            payment.Status = PaymentStatus.Refunded;
            return records;
        }, ct);
    }

    private Task AppendPaymentAsync(PaymentTransaction payment, CancellationToken ct) =>
        store.UpdateAsync<PaymentTransaction>(DataFiles.Payments, records => { records.Add(payment); return records; }, ct);
}

public interface IEmailService
{
    Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct = default);
}

public sealed class EmailService(JsonFileStore store, IReadOnlyDictionary<int, int>? failAttempts = null) : IEmailService
{
    private readonly IReadOnlyDictionary<int, int> _failAttempts = failAttempts ?? new Dictionary<int, int>();
    private readonly ConcurrentDictionary<int, int> _attempts = new();

    public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct = default)
    {
        int attempt = _attempts.AddOrUpdate(order.Id, 1, static (_, current) => current + 1);
        await SimulatedLatency.SendEmailAsync($"EmailService Order #{order.Id} attempt {attempt}", ct);

        if (_failAttempts.TryGetValue(order.Id, out int failCount) && attempt <= failCount)
            throw new SmtpUnavailableException($"SMTP unavailable on attempt {attempt}");

        await store.UpdateAsync<EmailMessage>(DataFiles.Emails, emails =>
        {
            emails.Add(new EmailMessage
            {
                Id = Guid.NewGuid(), OrderId = order.Id, To = customer.Email,
                Subject = $"Order #{order.Id} confirmation",
                Body = $"Your order #{order.Id} is confirmed. Total: {order.TotalAmount:F2}",
                SentAt = DateTime.Now, Attempts = attempt, LastError = null
            });
            return emails;
        }, ct);
    }
}

public sealed class OrderProcessor(
    CustomerService customers,
    InventoryService inventory,
    PricingService pricing,
    IPaymentGateway paymentGateway,
    IEmailService emailService,
    JsonFileStore store,
    OrderRepository orders,
    IEnumerable<int>? failSaveForOrderIds = null)
{
    private readonly HashSet<int> _failSaveIds = new(failSaveForOrderIds ?? []);

    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct = default)
    {
        bool stockReserved = false;
        string? capturedTransaction = null;
        var timer = Stopwatch.StartNew();

        try
        {
            Log.Info("OrderProcessor", $"Order #{order.Id} Step 1/6: customer validation");
            Customer customer;
            try { customer = await customers.GetCustomerAsync(order.CustomerId, ct); }
            catch (CustomerNotFoundException ex)
            {
                order.Status = OrderStatus.Rejected; order.FailureReason = ex.Message;
                await SaveAsync(order, ct); return false;
            }
            if (!customer.IsActive)
            {
                order.Status = OrderStatus.Rejected; order.FailureReason = "Customer is inactive";
                await SaveAsync(order, ct); return false;
            }

            Log.Info("OrderProcessor", $"Order #{order.Id} Step 2/6: inventory reservation");
            stockReserved = await inventory.ReserveStockAsync(order, ct);
            if (!stockReserved)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason ??= "Insufficient stock";
                await SaveAsync(order, ct); return false;
            }

            Log.Info("OrderProcessor", $"Order #{order.Id} Step 3/6: pricing");
            try { await pricing.CalculateTotalAsync(order, ct); }
            catch (InvalidCouponException ex)
            {
                await inventory.ReleaseStockAsync(order, CancellationToken.None);
                stockReserved = false;
                order.Status = OrderStatus.Rejected; order.FailureReason = ex.Message;
                await SaveAsync(order, ct); return false;
            }

            Log.Info("OrderProcessor", $"Order #{order.Id} Step 4/6: payment");
            using var paymentTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            paymentTimeout.CancelAfter(TimeSpan.FromSeconds(3));
            PaymentResult payment;
            try
            {
                payment = await paymentGateway.ProcessPaymentAsync(order.Id, order.TotalAmount, paymentTimeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && paymentTimeout.IsCancellationRequested)
            {
                await inventory.ReleaseStockAsync(order, CancellationToken.None);
                stockReserved = false;
                order.Status = OrderStatus.PaymentFailed;
                order.FailureReason = "Payment gateway timeout (3s)";
                await SaveAsync(order, CancellationToken.None);
                return false;
            }

            if (!payment.Success)
            {
                await inventory.ReleaseStockAsync(order, CancellationToken.None);
                stockReserved = false;
                order.Status = OrderStatus.PaymentFailed;
                order.FailureReason = payment.Message ?? "Card declined by issuer";
                await SaveAsync(order, ct); return false;
            }

            capturedTransaction = payment.TransactionId;
            order.TransactionId = capturedTransaction;
            order.Status = OrderStatus.Completed;
            Log.Info("OrderProcessor", $"Order #{order.Id} Step 5/6: save completed order");
            try { await SaveAsync(order, ct); }
            catch (DatabaseWriteException)
            {
                await paymentGateway.RefundAsync(capturedTransaction!, CancellationToken.None);
                await inventory.ReleaseStockAsync(order, CancellationToken.None);
                stockReserved = false;
                order.Status = OrderStatus.Failed;
                order.FailureReason = $"Database save failed after payment; payment {capturedTransaction} refunded";
                await SaveAsync(order, CancellationToken.None);
                return false;
            }

            Log.Info("OrderProcessor", $"Order #{order.Id} Step 6/6: confirmation email");
            await SendConfirmationWithRetryAsync(order, customer, ct);
            Log.Success("OrderProcessor", $"Order #{order.Id} completed in {timer.ElapsedMilliseconds} ms");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (capturedTransaction is not null)
                await paymentGateway.RefundAsync(capturedTransaction, CancellationToken.None);
            if (stockReserved)
                await inventory.ReleaseStockAsync(order, CancellationToken.None);

            order.Status = OrderStatus.Pending;
            order.FailureReason = "Cancelled";
            await SaveAsync(order, CancellationToken.None);
            return false;
        }
        catch (Exception ex)
        {
            if (capturedTransaction is not null)
            {
                try { await paymentGateway.RefundAsync(capturedTransaction, CancellationToken.None); } catch { }
            }
            if (stockReserved)
            {
                try { await inventory.ReleaseStockAsync(order, CancellationToken.None); } catch { }
            }
            order.Status = OrderStatus.Failed;
            order.FailureReason = ex.Message;
            await SaveAsync(order, CancellationToken.None);
            Log.Error("OrderProcessor", $"Order #{order.Id} failed: {ex.Message}");
            return false;
        }
    }

    private async Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct)
    {
        int[] waits = [500, 1000, 2000];
        string? lastError = null;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            if (attempt > 1) await Task.Delay(waits[attempt - 2], ct);
            try
            {
                await emailService.SendConfirmationAsync(order, customer, ct);
                return;
            }
            catch (SmtpUnavailableException ex)
            {
                lastError = ex.Message;
                Log.Error("EmailRetry", $"Order #{order.Id} email attempt {attempt} failed: {ex.Message}");
            }
        }

        await store.UpdateAsync<EmailMessage>(DataFiles.EmailRetry, records =>
        {
            records.Add(new EmailMessage
            {
                Id = Guid.NewGuid(), OrderId = order.Id, To = customer.Email,
                Subject = $"Order #{order.Id} confirmation", Body = $"Order #{order.Id} completed.",
                SentAt = DateTime.Now, Attempts = 4, LastError = lastError
            });
            return records;
        }, CancellationToken.None);
        Log.Error("EmailRetry", $"Email for Order #{order.Id} queued for retry later");
    }

    private async Task SaveAsync(Order order, CancellationToken ct)
    {
        if (order.Id == 13 && order.Status == OrderStatus.Completed && _failSaveIds.Contains(13))
            throw new DatabaseWriteException("Connection lost during commit");
        await orders.SaveAsync(order, ct);
    }
}

public sealed class Phase3Runner
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("\n========== PHASE 3: FULL CHECKOUT WORKFLOW ==========");
        await DataSeeder.ResetAsync(ct);

        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var store = new JsonFileStore(dataDirectory);
        var customerRepository = new CustomerRepository(store);
        var productRepository = new ProductRepository(store);
        var couponRepository = new CouponRepository(store);
        var incomingRepository = new IncomingOrderRepository(store);
        var orderRepository = new OrderRepository(store);
        var customerService = new CustomerService(customerRepository);
        var inventoryService = new InventoryService(productRepository);
        var pricingService = new PricingService(store, customerRepository, couponRepository);
        IPaymentGateway gateway = new PaymentGateway(store, [5], [8]);
        IEmailService email = new EmailService(store, new Dictionary<int, int>
        {
            [3] = 2,
            [17] = int.MaxValue
        });
        var processor = new OrderProcessor(customerService, inventoryService, pricingService,
            gateway, email, store, orderRepository, [13]);

        List<Order> incoming = await incomingRepository.GetAllAsync(ct);
        var tasks = new List<Task<bool>>(incoming.Count);
        var timings = new ConcurrentDictionary<int, Stopwatch>();

        foreach (Order order in incoming)
        {
            ct.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            timings[order.Id] = timer;
            tasks.Add(ProcessAndLogAsync(order, timer, processor, ct));
        }

        int finished = 0;
        int cancelled = 0;
        for (int i = 0; i < tasks.Count; i++)
        {
            try
            {
                bool result = await tasks[i];
                if (ct.IsCancellationRequested) cancelled++;
                else finished++;
                Console.WriteLine($"Order #{incoming[i].Id}: {(result ? "Completed" : incoming[i].Status)} â€” {incoming[i].FailureReason ?? "OK"}");
            }
            catch (OperationCanceledException)
            {
                cancelled++;
            }
        }

        List<Order> all = await orderRepository.GetAllAsync(CancellationToken.None);
        Console.WriteLine("\n========== PHASE 3 SUMMARY ==========");
        Console.WriteLine($"Orders saved: {all.Count}");
        foreach (OrderStatus status in Enum.GetValues<OrderStatus>())
            Console.WriteLine($"{status,-14} {all.Count(o => o.Status == status)}");

        List<PaymentTransaction> payments = await store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, CancellationToken.None);
        List<EmailMessage> emails = await store.ReadAllAsync<EmailMessage>(DataFiles.Emails, CancellationToken.None);
        List<EmailMessage> retries = await store.ReadAllAsync<EmailMessage>(DataFiles.EmailRetry, CancellationToken.None);
        List<Product> products = await store.ReadAllAsync<Product>(DataFiles.Products, CancellationToken.None);
        Console.WriteLine($"Payments: {payments.Count} (Captured {payments.Count(p => p.Status == PaymentStatus.Captured)}, Declined {payments.Count(p => p.Status == PaymentStatus.Declined)}, Refunded {payments.Count(p => p.Status == PaymentStatus.Refunded)})");
        Console.WriteLine($"Emails sent: {emails.Count}; queued for retry: {retries.Count}");
        Console.WriteLine($"Mechanical Keyboard stock: {products.First(p => p.Id == 4).StockQuantity}");
        if (ct.IsCancellationRequested)
            Console.WriteLine($"Run cancelled â€” {finished} orders finished, {cancelled} cancelled.");
        else
            Console.WriteLine("Expected baseline: 20 orders; 11 Completed, 6 Rejected, 2 PaymentFailed, 1 Failed.");
    }

    private static async Task<bool> ProcessAndLogAsync(Order order, Stopwatch timer, OrderProcessor processor, CancellationToken ct)
    {
        try { return await processor.ProcessOrderAsync(order, ct); }
        finally { timer.Stop(); }
    }
}
