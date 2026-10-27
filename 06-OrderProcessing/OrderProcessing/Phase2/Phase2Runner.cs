using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public sealed class Phase2Runner
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine(
            "\n========== PHASE 2: CONCURRENT ORDER PROCESSING ==========");

        await DataSeeder.ResetAsync(ct);

        string dataDirectory = Path.Combine(
            AppContext.BaseDirectory, "Data");

        var store = new JsonFileStore(dataDirectory);
        var orders = new OrderRepository(store);
        var products = new ProductRepository(store);
        var customers = new CustomerRepository(store);
        var coupons = new CouponRepository(store);
        var incomingRepository = new IncomingOrderRepository(store);

        var validator = new OrderValidator(
            customers, products, coupons, orders);

        List<Order> incoming =
            await incomingRepository.GetAllAsync(ct);

        // Validation runs concurrently; inventory is reserved only
        // during processing, where JsonFileStore protects each update.
        await Task.WhenAll(incoming.Select(async order =>
        {
            await validator.ValidateOrderAsync(order, ct);
            await orders.SaveAsync(order, ct);
        }));

        List<Order> validated =
            await orders.GetByStatusAsync(OrderStatus.Validated, ct);

        // Deterministic failures make the shipping-failure path testable.
        var pricing = new PricingService(
            store, customers, coupons, new[] { 6, 14 });

        var pricer = new OrderPricer(pricing);
        var invoices = new InvoiceGenerator();

        var processor = new OrderProcessorV2(
            products, pricer, invoices, orders);

        int completed = 0;
        int failed = 0;

        await Task.WhenAll(validated.Select(async order =>
        {
            bool success =
                await processor.ProcessValidatedOrderAsync(order, ct);

            if (success)
                Interlocked.Increment(ref completed);
            else
                Interlocked.Increment(ref failed);
        }));

        List<Order> all = await orders.GetAllAsync(ct);

        List<Order> rejected = all
            .Where(x => x.Status == OrderStatus.Rejected)
            .ToList();

        List<Order> completedOrders = all
            .Where(x => x.Status == OrderStatus.Completed)
            .ToList();

        List<Order> failedOrders = all
            .Where(x => x.Status == OrderStatus.Failed)
            .ToList();

        Console.WriteLine("\n========== PHASE 2 SUMMARY ==========");
        Console.WriteLine($"Incoming orders: {incoming.Count}");
        Console.WriteLine($"Completed:       {completedOrders.Count}");
        Console.WriteLine($"Rejected:        {rejected.Count}");
        Console.WriteLine($"Failed:          {failedOrders.Count}");

        foreach (Order order in rejected.Concat(failedOrders).OrderBy(x => x.Id))
            Console.WriteLine(
                $"Order #{order.Id}: {order.Status} - {order.FailureReason}");

        Console.WriteLine(
            $"Processing results: {completed} succeeded, {failed} unsuccessful.");

        Log.Success("Phase2Runner", "Phase 2 run finished.");
    }
}
