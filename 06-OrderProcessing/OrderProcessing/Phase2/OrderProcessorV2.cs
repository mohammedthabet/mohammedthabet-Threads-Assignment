using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public sealed class OrderProcessorV2
{
    private readonly ProductRepository _products;
    private readonly OrderPricer _pricer;
    private readonly InvoiceGenerator _invoiceGenerator;
    private readonly OrderRepository _orders;

    public OrderProcessorV2(
        ProductRepository products,
        OrderPricer pricer,
        InvoiceGenerator invoiceGenerator,
        OrderRepository orders)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(pricer);
        ArgumentNullException.ThrowIfNull(invoiceGenerator);
        ArgumentNullException.ThrowIfNull(orders);

        _products = products;
        _pricer = pricer;
        _invoiceGenerator = invoiceGenerator;
        _orders = orders;
    }

    public async Task<bool> ProcessValidatedOrderAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status != OrderStatus.Validated)
            throw new InvalidOperationException(
                $"Order #{order.Id} must be validated first.");

        var reserved = new List<OrderItem>();

        try
        {
            foreach (OrderItem item in order.Items)
            {
                ct.ThrowIfCancellationRequested();

                Product? product =
                    await _products.GetByIdAsync(item.ProductId, ct);

                if (product is null ||
                    !await _products.ReserveStockAsync(
                        item.ProductId, item.Quantity, ct))
                {
                    await RollbackAsync(reserved);

                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = product is null
                        ? $"Product #{item.ProductId} not found"
                        : $"Insufficient stock for '{product.Name}'";

                    await _orders.SaveAsync(order, ct);

                    Log.Info(
                        "OrderProcessorV2",
                        $"Order #{order.Id} rejected.");

                    return false;
                }

                reserved.Add(item);
            }

            await _pricer.PriceOrderAsync(order, ct);
            await _invoiceGenerator.GenerateAsync(order, ct);

            order.Status = OrderStatus.Completed;
            order.FailureReason = null;

            await _orders.SaveAsync(order, ct);

            Log.Success(
                "OrderProcessorV2",
                $"Order #{order.Id} completed.");

            return true;
        }
        catch (ShippingProviderUnavailableException ex)
        {
            await RollbackAsync(reserved);

            order.Status = OrderStatus.Failed;
            order.FailureReason = ex.Message;

            await _orders.SaveAsync(order, CancellationToken.None);

            Log.Error(
                "OrderProcessorV2",
                $"Order #{order.Id} failed because shipping is unavailable.");

            return false;
        }
        catch
        {
            // Roll back inventory even when processing or invoice generation fails.
            await RollbackAsync(reserved);
            throw;
        }
    }

    private async Task RollbackAsync(List<OrderItem> reserved)
    {
        for (int i = reserved.Count - 1; i >= 0; i--)
        {
            OrderItem item = reserved[i];

            bool released = await _products.ReleaseStockAsync(
                item.ProductId,
                item.Quantity,
                CancellationToken.None);

            if (!released)
            {
                throw new InvalidOperationException(
                    $"Inventory rollback failed for Product #{item.ProductId}.");
            }
        }

        reserved.Clear();
    }
}
