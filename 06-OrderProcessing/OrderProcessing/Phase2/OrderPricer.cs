using System.Diagnostics;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public sealed class OrderPricer
{
    private readonly PricingService _pricingService;

    public OrderPricer(PricingService pricingService)
    {
        ArgumentNullException.ThrowIfNull(pricingService);
        _pricingService = pricingService;
    }

    public async Task PriceOrderAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await _pricingService.CalculateSubTotalAsync(order, ct);

        Task<decimal> discountTask =
            _pricingService.GetDiscountAsync(order, ct);

        Task<decimal> shippingTask =
            _pricingService.CalculateShippingCostAsync(order, ct);

        await Task.WhenAll(discountTask, shippingTask);

        order.TotalAmount = Math.Round(
            order.SubTotal - order.Discount + order.ShippingCost,
            2,
            MidpointRounding.AwayFromZero);

        Log.Success(
            "OrderPricer",
            $"Order #{order.Id} total: {order.TotalAmount:F2}");
    }

    public async Task PriceOrderSequentialAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await _pricingService.CalculateSubTotalAsync(order, ct);
        await _pricingService.GetDiscountAsync(order, ct);
        await _pricingService.CalculateShippingCostAsync(order, ct);

        order.TotalAmount = Math.Round(
            order.SubTotal - order.Discount + order.ShippingCost,
            2,
            MidpointRounding.AwayFromZero);

        Log.Success(
            "OrderPricer",
            $"Sequential pricing for Order #{order.Id}: {order.TotalAmount:F2}");
    }

    public async Task ComparePricingAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        Stopwatch timer = Stopwatch.StartNew();

        await PriceOrderSequentialAsync(order, ct);

        timer.Stop();
        TimeSpan sequentialTime = timer.Elapsed;

        timer.Restart();

        await PriceOrderAsync(order, ct);

        timer.Stop();

        Console.WriteLine();
        Console.WriteLine("========== PRICING COMPARISON ==========");
        Console.WriteLine($"Sequential: {sequentialTime.TotalSeconds:F2} seconds");
        Console.WriteLine($"Concurrent: {timer.Elapsed.TotalSeconds:F2} seconds");
        Console.WriteLine($"Subtotal:   {order.SubTotal:F2}");
        Console.WriteLine($"Discount:   {order.Discount:F2}");
        Console.WriteLine($"Shipping:   {order.ShippingCost:F2}");
        Console.WriteLine($"Total:      {order.TotalAmount:F2}");
    }
}
