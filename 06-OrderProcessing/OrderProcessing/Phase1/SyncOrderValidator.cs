using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1;

public sealed class SyncOrderValidator
{
    private readonly SyncCustomerRepository _customers;
    private readonly SyncProductRepository _products;
    private readonly SyncCouponRepository _coupons;
    private const string Source = "SyncOrderValidator";

    public SyncOrderValidator(
        SyncCustomerRepository customers,
        SyncProductRepository products,
        SyncCouponRepository coupons)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(coupons);
        _customers = customers;
        _products = products;
        _coupons = coupons;
    }

    public Order ValidateOrder(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.FailureReason = null;

        Customer? customer = _customers.GetById(order.CustomerId);
        if (customer is null)
            return Reject(order, $"Customer #{order.CustomerId} not found");

        if (!customer.IsActive)
            return Reject(order, "Customer is inactive");

        var productsById = new Dictionary<int, Product>();

        foreach (OrderItem item in order.Items)
        {
            if (productsById.ContainsKey(item.ProductId))
                continue;

            Product? product = _products.GetById(item.ProductId);
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
            Coupon? coupon = _coupons.GetByCode(order.CouponCode);
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
