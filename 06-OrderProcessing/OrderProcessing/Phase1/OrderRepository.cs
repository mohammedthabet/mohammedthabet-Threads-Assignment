using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class OrderRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "OrderRepository";

    public OrderRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task SaveAsync(
        Order order,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(order.Id);

        await SimulatedLatency.DatabaseWriteAsync(
            Source,
            $"Save Order #{order.Id}",
            ct);

        await _store.UpdateAsync<Order>(
            DataFiles.Orders,
            orders =>
            {
                int index = orders.FindIndex(
                    existing => existing.Id == order.Id);

                if (index >= 0)
                {
                    orders[index] = order;
                }
                else
                {
                    orders.Add(order);
                }

                return orders;
            },
            ct);

        await SimulatedLatency.CommitAsync(Source, ct);

        Log.Success(Source, $"Order #{order.Id} saved.");
    }

    public async Task<List<Order>> GetAllAsync(
        CancellationToken ct = default)
    {
        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            "Get All Orders",
            ct);

        List<Order> orders =
            await _store.ReadAllAsync<Order>(
                DataFiles.Orders,
                ct);

        Log.Info(Source, $"Retrieved {orders.Count} order(s).");

        return orders;
    }

    public async Task<List<Order>> GetByStatusAsync(
        OrderStatus status,
        CancellationToken ct = default)
    {
        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            $"Get Orders by Status '{status}'",
            ct);

        List<Order> orders =
            await _store.ReadAllAsync<Order>(
                DataFiles.Orders,
                ct);

        List<Order> filteredOrders = orders
            .Where(order => order.Status == status)
            .ToList();

        Log.Info(
            Source,
            $"Found {filteredOrders.Count} order(s) with status '{status}'.");

        return filteredOrders;
    }
}
