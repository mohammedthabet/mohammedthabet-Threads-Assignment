using System.Diagnostics;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class Phase1Runner
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("\n========== PHASE 1: SYNC VS ASYNC ==========");

        await DataSeeder.ResetAsync(ct);

        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var store = new JsonFileStore(dataDirectory);
        var incomingRepository = new IncomingOrderRepository(store);

        List<Order> incomingOrders = await incomingRepository.GetAllAsync(ct);
        Console.WriteLine($"\nLoaded incoming orders: {incomingOrders.Count}");

        var syncValidator = new SyncOrderValidator(
            new SyncCustomerRepository(store),
            new SyncProductRepository(store),
            new SyncCouponRepository(store));

        Stopwatch syncTimer = Stopwatch.StartNew();

        foreach (Order order in incomingOrders)
        {
            ct.ThrowIfCancellationRequested();
            syncValidator.ValidateOrder(order);
        }

        syncTimer.Stop();
        Console.WriteLine($"\nSync validation completed in {syncTimer.Elapsed.TotalSeconds:F1} seconds.");
        Console.WriteLine("Sync results were not saved.");

        await DataSeeder.ResetAsync(ct);

        incomingOrders = await incomingRepository.GetAllAsync(ct);

        var customerRepository = new CustomerRepository(store);
        var productRepository = new ProductRepository(store);
        var couponRepository = new CouponRepository(store);
        var orderRepository = new OrderRepository(store);
        var asyncValidator = new OrderValidator(
            customerRepository,
            productRepository,
            couponRepository,
            orderRepository);

        Stopwatch asyncTimer = Stopwatch.StartNew();
        Stopwatch asyncValidationTimer = new();
        Stopwatch asyncSaveTimer = new();

        foreach (Order order in incomingOrders)
        {
            ct.ThrowIfCancellationRequested();

            asyncValidationTimer.Start();
            await asyncValidator.ValidateOrderAsync(order, ct);
            asyncValidationTimer.Stop();

            asyncSaveTimer.Start();
            await orderRepository.SaveAsync(order, ct);
            asyncSaveTimer.Stop();
        }

        asyncTimer.Stop();

        List<Order> validated = await orderRepository.GetByStatusAsync(OrderStatus.Validated, ct);
        List<Order> rejected = await orderRepository.GetByStatusAsync(OrderStatus.Rejected, ct);
        List<Order> allOrders = await orderRepository.GetAllAsync(ct);

        Console.WriteLine("\n========== VALIDATION SUMMARY ==========");
        Console.WriteLine($"Total Orders: {allOrders.Count}");
        Console.WriteLine($"Validated:    {validated.Count}");
        Console.WriteLine($"Rejected:     {rejected.Count}");

        foreach (Order order in rejected.OrderBy(item => item.Id))
            Console.WriteLine($"  - Order #{order.Id}: {order.FailureReason}");

        Console.WriteLine($"Sync Time:  {syncTimer.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async Time: {asyncValidationTimer.Elapsed.TotalSeconds:F1} seconds (validation only)");
        Console.WriteLine($"Async Save Time: {asyncSaveTimer.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async Total Time: {asyncTimer.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"orders.json now contains {allOrders.Count} records.");

        bool passed =
            allOrders.Count == 20 &&
            validated.Count == 15 &&
            rejected.Count == 5 &&
            rejected.Any(order => order.Id == 4 && order.FailureReason == "Customer is inactive") &&
            rejected.Any(order => order.Id == 9 && order.FailureReason == "Insufficient stock for 'Wireless Mouse'") &&
            rejected.Any(order => order.Id == 12 && order.FailureReason == "Coupon 'OLD20' has expired") &&
            rejected.Any(order => order.Id == 15 && order.FailureReason == "Customer #99 not found") &&
            rejected.Any(order => order.Id == 18 && order.FailureReason == "Coupon 'FAKE50' does not exist");

        if (!passed)
            throw new InvalidOperationException("Phase 1 validation did not match the expected results.");

        Log.Success("Phase1Runner", "PASS: All validation results match.");
    }
}
