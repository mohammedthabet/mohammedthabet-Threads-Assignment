using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public sealed class PricingService
{
    private const string Source = "PricingService";

    private readonly JsonFileStore _store;
    private readonly CustomerRepository _customers;
    private readonly CouponRepository _coupons;
    private readonly HashSet<int> _failShippingForOrderIds;

    public PricingService(
        JsonFileStore store,
        CustomerRepository customers,
        CouponRepository coupons,
        IEnumerable<int> failShippingForOrderIds)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(coupons);
        ArgumentNullException.ThrowIfNull(failShippingForOrderIds);

        _store = store;
        _customers = customers;
        _coupons = coupons;
        _failShippingForOrderIds = new HashSet<int>(
            failShippingForOrderIds);
    }

    public async Task<decimal> CalculateSubTotalAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            $"Calculate subtotal for Order #{order.Id}",
            ct);

        List<Product> products =
            await _store.ReadAllAsync<Product>(
                DataFiles.Products, ct);

        decimal subtotal = 0m;

        foreach (OrderItem item in order.Items)
        {
            Product? product = products.FirstOrDefault(
                p => p.Id == item.ProductId);

            if (product is null)
                throw new InvalidOperationException(
                    $"Product #{item.ProductId} not found.");

            item.UnitPrice = product.Price;
            subtotal += product.Price * item.Quantity;
        }

        order.SubTotal = RoundMoney(subtotal);

        Log.Success(
            Source,
            $"Order #{order.Id} subtotal: {order.SubTotal:F2}");

        return order.SubTotal;
    }

    public async Task<decimal> GetDiscountAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await SimulatedLatency.ExternalApiAsync(
            Source,
            $"Calculate discount for Order #{order.Id}",
            ct);

        Customer? customer =
            await _customers.GetByIdAsync(order.CustomerId, ct);

        if (customer is null)
            throw new InvalidOperationException(
                $"Customer #{order.CustomerId} not found.");

        int loyaltyPercent = customer.LoyaltyTier switch
        {
            LoyaltyTier.Bronze => 0,
            LoyaltyTier.Silver => 5,
            LoyaltyTier.Gold => 10,
            _ => throw new InvalidOperationException(
                "Unknown loyalty tier.")
        };

        int couponPercent = 0;

        if (order.CouponCode is not null)
        {
            Coupon? coupon =
                await _coupons.GetByCodeAsync(order.CouponCode, ct);

            if (coupon is null)
                throw new InvalidOperationException(
                    $"Coupon '{order.CouponCode}' does not exist.");

            if (coupon.ExpiryDate.Date < DateTime.Today)
                throw new InvalidOperationException(
                    $"Coupon '{order.CouponCode}' has expired.");

            couponPercent = coupon.DiscountPercent;
        }

        int discountPercent = Math.Min(
            30, loyaltyPercent + couponPercent);

        decimal discount = RoundMoney(
            order.SubTotal * discountPercent / 100m);

        order.Discount = discount;

        Log.Success(
            Source,
            $"Order #{order.Id} discount: {discount:F2} ({discountPercent}%)");

        return discount;
    }

    public async Task<decimal> CalculateShippingCostAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        Log.Info(
            Source,
            $"Order #{order.Id} shipping: DNS/handshake...");

        await Task.Delay(150, ct);

        if (_failShippingForOrderIds.Contains(order.Id))
        {
            Log.Error(
                Source,
                $"Order #{order.Id}: shipping provider unavailable.");

            throw new ShippingProviderUnavailableException(
                "Shipping provider unavailable");
        }

        Log.Info(
            Source,
            $"Order #{order.Id} shipping: Sending request...");

        await Task.Delay(300, ct);

        Log.Info(
            Source,
            $"Order #{order.Id} shipping: Waiting for response...");

        await Task.Delay(Random.Shared.Next(500, 1501), ct);

        List<Product> products =
            await _store.ReadAllAsync<Product>(
                DataFiles.Products, ct);

        decimal totalWeight = 0m;
        int totalQuantity = 0;

        foreach (OrderItem item in order.Items)
        {
            Product? product = products.FirstOrDefault(
                p => p.Id == item.ProductId);

            if (product is null)
                throw new InvalidOperationException(
                    $"Product #{item.ProductId} not found.");

            totalWeight += product.WeightKg * item.Quantity;
            totalQuantity += item.Quantity;
        }

        decimal shipping = order.SubTotal >= 1000m
            ? 0m
            : 5m + 1.50m * totalQuantity + 2m * totalWeight;

        order.ShippingCost = RoundMoney(shipping);

        Log.Success(
            Source,
            $"Order #{order.Id} shipping cost: {order.ShippingCost:F2}");

        return order.ShippingCost;
    }

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
