using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class SyncCustomerRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "SyncCustomerRepository";

    public SyncCustomerRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Customer? GetById(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Log.Info(Source, $"Get Customer #{id}: Opening connection...");
        Thread.Sleep(200);
        Log.Info(Source, $"Get Customer #{id}: Executing query...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Customer? customer = _store.ReadAll<Customer>(DataFiles.Customers)
            .FirstOrDefault(item => item.Id == id);

        if (customer is null)
            Log.Error(Source, $"Customer #{id} not found.");
        else
            Log.Success(Source, $"Customer #{id} found ({customer.Name}).");

        return customer;
    }
}
