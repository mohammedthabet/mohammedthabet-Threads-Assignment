using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1;

public sealed class OrderValidator
{
    private readonly CustomerRepository _customers;
    private readonly ProductRepository _products;
    private readonly CouponRepository _coupons;
    private const string Source = "OrderValidator";

    public OrderValidator(
        CustomerRepository customers,
        ProductRepository products,
        CouponRepository coupons,
        OrderRepository orders)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(coupons);
        ArgumentNullException.ThrowIfNull(orders);

        _customers = customers;
        _products = products;
        _coupons = coupons;
    }

    public async Task<Order> ValidateOrderAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.FailureReason = null;

        Customer? customer = await _customers.GetByIdAsync(order.CustomerId, ct);

        if (customer is null)
            return Reject(order, $"Customer #{order.CustomerId} not found");

        if (!customer.IsActive)
            return Reject(order, "Customer is inactive");

        var productsById = new Dictionary<int, Product>();

        foreach (OrderItem item in order.Items)
        {
            if (productsById.ContainsKey(item.ProductId))
                continue;

            Product? product = await _products.GetByIdAsync(item.ProductId, ct);

            if (product is null)
                return Reject(order, $"Product #{item.ProductId} not found");

            productsById.Add(product.Id, product);
        }

        foreach (OrderItem item in order.Items)
        {
            Product product = productsById[item.ProductId];

            if (product.StockQuantity < item.Quantity)
                return Reject(order, $"Insufficient stock for '{product.Name}'");
        }

        if (order.CouponCode is not null)
        {
            Coupon? coupon = await _coupons.GetByCodeAsync(order.CouponCode, ct);

            if (coupon is null)
                return Reject(order, $"Coupon '{order.CouponCode}' does not exist");

            if (coupon.ExpiryDate < DateTime.Today)
                return Reject(order, $"Coupon '{order.CouponCode}' has expired");
        }

        order.Status = OrderStatus.Validated;
        order.FailureReason = null;
        Log.Success(Source, $"Order #{order.Id} validated.");
        return order;
    }

    private static Order Reject(Order order, string reason)
    {
        order.Status = OrderStatus.Rejected;
        order.FailureReason = reason;
        Log.Error(Source, $"Order #{order.Id} rejected: {reason}");
        return order;
    }
}
