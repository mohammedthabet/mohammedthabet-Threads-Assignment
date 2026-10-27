using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Phase3;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4;

public sealed class CustomerDashboard
{
    public Customer? Customer { get; init; }
    public List<Order> Orders { get; init; } = [];
    public List<PaymentTransaction> Payments { get; init; } = [];
    public List<EmailMessage> Emails { get; init; } = [];
}

public sealed record ShippingQuote(string Carrier, decimal Price, long ElapsedMs);

public sealed class CustomerDashboardService(
    JsonFileStore store,
    CustomerRepository customers,
    IEnumerable<int>? failPaymentsForCustomerIds = null)
{
    private readonly HashSet<int> _failPayments = new(failPaymentsForCustomerIds ?? []);

    public async Task<CustomerDashboard> LoadAsync(int customerId, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        Task<Customer?> profileTask = LoadProfileAsync(customerId, ct);
        Task<List<Order>> ordersTask = LoadOrdersAsync(customerId, ct);
        Task<List<PaymentTransaction>> paymentsTask = LoadPaymentsAsync(customerId, ct);
        Task<List<EmailMessage>> emailsTask = LoadEmailsAsync(customerId, ct);
        Task all = Task.WhenAll(profileTask, ordersTask, paymentsTask, emailsTask);

        try
        {
            await all;
        }
        catch
        {
            ct.ThrowIfCancellationRequested();

            Exception[] failures = new Task[] { profileTask, ordersTask, paymentsTask, emailsTask }
                .Where(t => t.IsFaulted)
                .SelectMany(t => t.Exception!.Flatten().InnerExceptions)
                .ToArray();

            throw new AggregateException($"Customer dashboard load failed for customer #{customerId}.", failures);
        }

        timer.Stop();
        Console.WriteLine($"Dashboard loaded in {timer.Elapsed.TotalSeconds:F2} s (parallel loads; near slowest load).");
        return new CustomerDashboard
        {
            Customer = await profileTask,
            Orders = await ordersTask,
            Payments = await paymentsTask,
            Emails = await emailsTask
        };
    }

    private async Task<Customer?> LoadProfileAsync(int id, CancellationToken ct)
    {
        await SimulatedLatency.DatabaseQueryAsync("CustomerDashboard", $"Profile #{id}", ct);
        return await customers.GetByIdAsync(id, ct)
            ?? throw new CustomerNotFoundException($"Customer #{id} not found");
    }

    private async Task<List<Order>> LoadOrdersAsync(int customerId, CancellationToken ct)
    {
        await SimulatedLatency.DatabaseQueryAsync("CustomerDashboard", $"Orders for customer #{customerId}", ct);
        List<Order> all = await store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        return all.Where(o => o.CustomerId == customerId).OrderBy(o => o.Id).ToList();
    }

    private async Task<List<PaymentTransaction>> LoadPaymentsAsync(int customerId, CancellationToken ct)
    {
        await SimulatedLatency.DatabaseQueryAsync("CustomerDashboard", $"Payments for customer #{customerId}", ct);
        if (_failPayments.Contains(customerId))
            throw new TimeoutException("Payments store timeout");
        List<Order> orders = await store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        HashSet<int> ids = orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();
        List<PaymentTransaction> payments = await store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct);
        return payments.Where(p => ids.Contains(p.OrderId)).OrderBy(p => p.OrderId).ToList();
    }

    private async Task<List<EmailMessage>> LoadEmailsAsync(int customerId, CancellationToken ct)
    {
        await SimulatedLatency.DatabaseQueryAsync("CustomerDashboard", $"Emails for customer #{customerId}", ct);
        List<Order> orders = await store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        HashSet<int> ids = orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();
        List<EmailMessage> emails = await store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct);
        return emails.Where(e => ids.Contains(e.OrderId)).OrderBy(e => e.OrderId).ToList();
    }
}

public interface IShippingCarrier
{
    string Name { get; }
    Task<decimal> GetQuoteAsync(Order order, CancellationToken ct = default);
}

public sealed class ShippingCarrier(
    string name,
    int minDelayMs,
    int maxDelayMs,
    decimal priceFactor,
    IEnumerable<int>? failForOrderIds = null) : IShippingCarrier
{
    private readonly HashSet<int> _failIds = new(failForOrderIds ?? []);
    public string Name { get; } = name;

    public async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(150, ct);
            if (_failIds.Contains(order.Id))
                throw new ShippingProviderUnavailableException($"{Name} unavailable for order #{order.Id}");
            await Task.Delay(Random.Shared.Next(minDelayMs, maxDelayMs + 1), ct);
            if (Name == "CairoExpress" && order.Id % 2 == 0)
                throw new ShippingProviderUnavailableException($"{Name} unavailable for order #{order.Id}");

            return Math.Round(order.ShippingCost * priceFactor, 2, MidpointRounding.AwayFromZero);
        }
        catch (OperationCanceledException)
        {
            Log.Info(Name, "cancelled");
            throw;
        }
    }
}

public sealed class ShippingQuoteService
{
    public async Task<ShippingQuote> GetFastestQuoteAsync(
        Order order,
        CancellationToken ct = default,
        IEnumerable<int>? failForOrderIds = null)
    {
        var carriers = new IShippingCarrier[]
        {
            new ShippingCarrier("FastShip", 500, 1500, 1.00m, failForOrderIds),
            new ShippingCarrier("CairoExpress", 300, 2000, 0.95m, failForOrderIds),
            new ShippingCarrier("NileCourier", 800, 1200, 1.10m, failForOrderIds)
        };
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(1200));
        var timer = Stopwatch.StartNew();
        var pending = carriers.ToDictionary(c => c, c => c.GetQuoteAsync(order, budget.Token));
        var errors = new List<Exception>();

        while (pending.Count > 0 && !budget.IsCancellationRequested)
        {
            Task<decimal> finished = await Task.WhenAny(pending.Values);
            IShippingCarrier carrier = pending.First(pair => ReferenceEquals(pair.Value, finished)).Key;
            pending.Remove(carrier);
            try
            {
                decimal price = await finished;
                timer.Stop();
                budget.Cancel();
                await ObserveLosersAsync(pending.Values);
                return new ShippingQuote(carrier.Name, price, timer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                budget.Cancel();
                await ObserveLosersAsync(pending.Values);
                throw;
            }
            catch (OperationCanceledException)
            {
                // The 1.2-second budget expired; return the flat rate below.
                break;
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                if (pending.Count == 0)
                    throw new ShippingProviderUnavailableException("All carriers failed");
            }
        }

        budget.Cancel();
        await ObserveLosersAsync(pending.Values);
        if (ct.IsCancellationRequested)
            ct.ThrowIfCancellationRequested();
        if (errors.Count == carriers.Length)
            throw new ShippingProviderUnavailableException("All carriers failed");
        timer.Stop();
        return new ShippingQuote("Flat rate", 25.00m, Math.Min(timer.ElapsedMilliseconds, 1200));
    }

    private static async Task ObserveLosersAsync(IEnumerable<Task<decimal>> tasks)
    {
        foreach (Task<decimal> task in tasks)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
    }
}

public sealed class Phase4Runner
{
    private const int FraudOrderCount = 10_000;
    private const int FraudThreshold = 90;

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("\n========== PHASE 4: SCALE-UP ==========");
        await DataSeeder.ResetAsync(ct);
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var store = new JsonFileStore(dataDirectory);
        var customers = new CustomerRepository(store);
        var products = new ProductRepository(store);
        var coupons = new CouponRepository(store);
        var incomingRepository = new IncomingOrderRepository(store);
        var orderRepository = new OrderRepository(store);
        var customerService = new CustomerService(customers);
        var inventory = new InventoryService(products);
        var pricing = new PricingService(store, customers, coupons);
        IPaymentGateway gateway = new PaymentGateway(store, [5], [8]);
        IEmailService email = new EmailService(store, new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue });
        var processor = new OrderProcessor(customerService, inventory, pricing, gateway, email, store, orderRepository, [13]);

        await RunLiveProgressAsync(incomingRepository, orderRepository, processor, ct);
        await RunDashboardAsync(store, customers, ct);
        await RunShippingQuotesAsync(orderRepository, store, ct);
        await RunFraudAndEmailAsync(store, email, ct);
        await SaveSampleOutputAsync(dataDirectory, ct);
        Console.WriteLine("\nPHASE 4 COMPLETE");
    }

    private static async Task RunLiveProgressAsync(
        IncomingOrderRepository incomingRepository,
        OrderRepository orderRepository,
        OrderProcessor processor,
        CancellationToken ct)
    {
        Console.WriteLine("\n========== TASK 10.1: LIVE CHECKOUT PROGRESS ==========");
        List<Order> incoming = await incomingRepository.GetAllAsync(ct);
        var timers = new ConcurrentDictionary<int, Stopwatch>();
        var taskToOrder = new Dictionary<Task<bool>, Order>();
        var threadIds = new ConcurrentDictionary<int, byte>();
        Stopwatch total = Stopwatch.StartNew();

        foreach (Order order in incoming)
        {
            ct.ThrowIfCancellationRequested();
            Stopwatch timer = Stopwatch.StartNew();
            timers[order.Id] = timer;
            Task<bool> task = ProcessOrderAndCaptureThreadAsync(order, processor, timer, threadIds, ct);
            taskToOrder.Add(task, order);
        }

        int completedLines = 0;
        await foreach (Task<bool> finished in Task.WhenEach(taskToOrder.Keys))
        {
            Order order = taskToOrder[finished];
            bool result = false;
            try { result = await finished; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { order.Status = OrderStatus.Failed; order.FailureReason = ex.Message; }
            completedLines++;
            Stopwatch timer = timers[order.Id];
            Console.WriteLine($"[{completedLines,2}/20] Order #{order.Id} -> {order.Status} ({order.FailureReason ?? (result ? "OK" : "Not completed")}) after {timer.Elapsed.TotalSeconds:F1} s");
        }

        total.Stop();
        List<Order> saved = await orderRepository.GetAllAsync(CancellationToken.None);
        Console.WriteLine($"Live checkout: {total.Elapsed.TotalSeconds:F2} s; distinct threads observed: {threadIds.Count}; ThreadPool.ThreadCount: {System.Threading.ThreadPool.ThreadCount}");
        Console.WriteLine("Status counts:");
        foreach (OrderStatus status in Enum.GetValues<OrderStatus>())
            Console.WriteLine($"{status,-14} {saved.Count(o => o.Status == status)}");
    }

    private static async Task<bool> ProcessOrderAndCaptureThreadAsync(
        Order order, OrderProcessor processor, Stopwatch timer,
        ConcurrentDictionary<int, byte> threadIds, CancellationToken ct)
    {
        threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
        try { return await processor.ProcessOrderAsync(order, ct); }
        finally { timer.Stop(); threadIds.TryAdd(Environment.CurrentManagedThreadId, 0); }
    }

    private static async Task RunDashboardAsync(JsonFileStore store, CustomerRepository customers, CancellationToken ct)
    {
        Console.WriteLine("\n========== TASK 10.2: CUSTOMER DASHBOARD ==========");
        var dashboardService = new CustomerDashboardService(store, customers, [99]);
        CustomerDashboard dashboard = await dashboardService.LoadAsync(3, ct);
        Customer customer = dashboard.Customer ?? throw new InvalidOperationException("Customer #3 missing.");
        Console.WriteLine($"{customer.Name} — {dashboard.Orders.Count} orders ({string.Join(", ", dashboard.Orders.Select(o => $"#{o.Id} {o.Status}"))}), {dashboard.Payments.Count} payments ({string.Join(", ", dashboard.Payments.Select(p => $"#{p.OrderId} {p.Status}"))}), {dashboard.Emails.Count} email(s) ({string.Join(", ", dashboard.Emails.Select(e => $"#{e.OrderId}"))})");

        try { await dashboardService.LoadAsync(99, ct); }
        catch (AggregateException ex)
        {
            Console.WriteLine("Customer #99 dashboard failures:");
            foreach (Exception inner in ex.Flatten().InnerExceptions)
                Console.WriteLine($"- {inner.Message}");
        }
    }

    private static async Task RunShippingQuotesAsync(OrderRepository orderRepository, JsonFileStore store, CancellationToken ct)
    {
        Console.WriteLine("\n========== TASK 10.3: FASTEST SHIPPING QUOTE ==========");
        List<Order> orders = await orderRepository.GetAllAsync(ct);
        List<Product> products = await store.ReadAllAsync<Product>(DataFiles.Products, ct);
        var service = new ShippingQuoteService();
        foreach (int id in Enumerable.Range(1, 5))
        {
            Order? order = orders.FirstOrDefault(o => o.Id == id);
            if (order is null) continue;
            decimal weight = order.Items.Sum(item => products.First(p => p.Id == item.ProductId).WeightKg * item.Quantity);
            decimal baseShipping = order.SubTotal >= 1000m ? 0m : 5m + 1.50m * order.Items.Sum(i => i.Quantity) + 2m * weight;
            order.ShippingCost = baseShipping;
            ShippingQuote quote = await service.GetFastestQuoteAsync(order, ct);
            Console.WriteLine($"Order #{id}: {quote.Carrier}, ${quote.Price:F2}, {quote.ElapsedMs} ms");
        }

        try
        {
            Order order6 = new() { Id = 6, SubTotal = 100m, Items = [new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 100m }] };
            _ = await service.GetFastestQuoteAsync(order6, ct, [6]);
        }
        catch (ShippingProviderUnavailableException ex)
        {
            Console.WriteLine($"Expected all-carriers-failed demo: {ex.Message}");
        }
    }

    private static int Score(int orderId)
    {
        long h = orderId;
        for (int i = 0; i < 100_000; i++)
            h = (h * 31 + i) % 1_000_000_007;
        return (int)(h % 100);
    }

    private static async Task RunFraudAndEmailAsync(JsonFileStore store, IEmailService email, CancellationToken ct)
    {
        Console.WriteLine("\n========== TASK 10.4A: FRAUD SCORING ==========");
        int[] ids = Enumerable.Range(1, FraudOrderCount).ToArray();
        var sequentialThreads = new ConcurrentDictionary<int, byte>();
        Stopwatch sequentialTimer = Stopwatch.StartNew();
        int[] sequentialFlagged = ids.Where(id => { sequentialThreads.TryAdd(Environment.CurrentManagedThreadId, 0); return Score(id) >= FraudThreshold; }).ToArray();
        sequentialTimer.Stop();

        var parallelThreads = new ConcurrentDictionary<int, byte>();
        var parallelFlagged = new ConcurrentBag<int>();
        Stopwatch parallelTimer = Stopwatch.StartNew();
        await Parallel.ForEachAsync(ids, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, (id, token) =>
        {
            token.ThrowIfCancellationRequested();
            parallelThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
            if (Score(id) >= FraudThreshold) parallelFlagged.Add(id);
            return ValueTask.CompletedTask;
        });
        parallelTimer.Stop();
        int[] sortedParallel = parallelFlagged.Order().ToArray();
        if (!sequentialFlagged.SequenceEqual(sortedParallel) || sequentialFlagged.Length != 1005 || !sequentialFlagged.Take(5).SequenceEqual([4, 8, 12, 29, 33]))
            throw new InvalidOperationException($"Fraud scoring validation failed: sequential={sequentialFlagged.Length}, parallel={sortedParallel.Length}.");
        double speedup = parallelTimer.Elapsed.TotalMilliseconds == 0 ? 0 : sequentialTimer.Elapsed.TotalMilliseconds / parallelTimer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"Flagged: {sortedParallel.Length}; sequential: {sequentialTimer.Elapsed.TotalMilliseconds:F0} ms; parallel: {parallelTimer.Elapsed.TotalMilliseconds:F0} ms; speed-up: {speedup:F2}x; parallel threads: {parallelThreads.Count}; CPU cores: {Environment.ProcessorCount}; ThreadPool.ThreadCount: {System.Threading.ThreadPool.ThreadCount}");

        Console.WriteLine("\n========== TASK 10.4B: EMAIL RE-SEND ==========");
        List<Customer> customers = await store.ReadAllAsync<Customer>(DataFiles.Customers, ct);
        Customer customer = customers.First(c => c.Id == 1);
        var active = 0;
        var maxActive = 0;
        Stopwatch emailTimer = Stopwatch.StartNew();
        await Parallel.ForEachAsync(Enumerable.Range(1001, 30), new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, async (id, token) =>
        {
            int current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maxActive, current);
            try
            {
                var order = new Order { Id = id, CustomerId = customer.Id, Status = OrderStatus.Completed, TotalAmount = 0m };
                await email.SendConfirmationAsync(order, customer, token);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        emailTimer.Stop();
        List<EmailMessage> allEmails = await store.ReadAllAsync<EmailMessage>(DataFiles.Emails, CancellationToken.None);
        int[] testCounts = allEmails.Where(e => e.OrderId is >= 1001 and <= 1030).GroupBy(e => e.OrderId).Select(g => g.Count()).ToArray();
        if (testCounts.Length != 30 || testCounts.Any(count => count != 1))
            throw new InvalidOperationException("Email re-send validation failed: expected 30 unique test emails exactly once.");
        Console.WriteLine($"Emails sent: 30; max simultaneous sends: {maxActive}; elapsed: {emailTimer.Elapsed.TotalSeconds:F2} s; validation: PASS");
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static async Task SaveSampleOutputAsync(string dataDirectory, CancellationToken ct)
    {
        string destination = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sample-output", "phase4"));
        Directory.CreateDirectory(destination);
        foreach (string fileName in new[] { DataFiles.Orders, DataFiles.Payments, DataFiles.Emails, DataFiles.EmailRetry, DataFiles.Products })
        {
            string source = Path.Combine(dataDirectory, fileName);
            if (File.Exists(source))
                await File.WriteAllBytesAsync(Path.Combine(destination, fileName), await File.ReadAllBytesAsync(source, ct), ct);
        }
        Console.WriteLine($"Phase 4 JSON snapshots saved to: {destination}");
    }
}
