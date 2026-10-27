using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class SyncProductRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "SyncProductRepository";

    public SyncProductRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Product? GetById(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        Log.Info(Source, $"Get Product #{id}: Opening connection...");
        Thread.Sleep(200);
        Log.Info(Source, $"Get Product #{id}: Executing query...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Product? product = _store.ReadAll<Product>(DataFiles.Products)
            .FirstOrDefault(item => item.Id == id);

        if (product is null)
            Log.Error(Source, $"Product #{id} not found.");
        else
            Log.Success(Source, $"Product #{id} found ({product.Name}).");

        return product;
    }
}
