using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class IncomingOrderRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "IncomingOrderRepository";

    public IncomingOrderRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<List<Order>> GetAllAsync(
        CancellationToken ct = default)
    {
        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            "Get Incoming Orders",
            ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(
            DataFiles.IncomingOrders,
            ct);

        Log.Success(Source, $"Retrieved {orders.Count} incoming order(s).");

        return orders;
    }
}
