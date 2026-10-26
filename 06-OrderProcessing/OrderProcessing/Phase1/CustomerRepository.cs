using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class CustomerRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "CustomerRepository";

    public CustomerRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Customer?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            $"Get Customer #{id}",
            ct);

        // Read only after the simulated database query finishes.
        List<Customer> customers =
            await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct);

        Customer? customer = customers.FirstOrDefault(
            customer => customer.Id == id);

        if (customer is null)
        {
            Log.Error(Source, $"Customer #{id} not found.");
            return null;
        }

        Log.Success(
            Source,
            $"Customer #{id} found ({customer.Name}).");

        return customer;
    }
}
