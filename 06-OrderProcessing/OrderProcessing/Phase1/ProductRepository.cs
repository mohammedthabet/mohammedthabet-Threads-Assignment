using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class ProductRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "ProductRepository";

    public ProductRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Product?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            $"Get Product #{id}",
            ct);

        List<Product> products =
            await _store.ReadAllAsync<Product>(DataFiles.Products, ct);

        Product? product = products.FirstOrDefault(
            product => product.Id == id);

        if (product is null)
        {
            Log.Error(Source, $"Product #{id} not found.");
            return null;
        }

        Log.Success(Source, $"Product #{id} found ({product.Name}).");
        return product;
    }

    public async Task<bool> ReserveStockAsync(
        int productId,
        int qty,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);

        // Simulate opening a connection and starting a write transaction.
        await SimulatedLatency.DatabaseWriteAsync(
            Source,
            $"Reserve {qty} unit(s) of Product #{productId}",
            ct);

        bool reserved = false;

        // UpdateAsync protects the complete read-modify-write operation.
        await _store.UpdateAsync<Product>(
            DataFiles.Products,
            products =>
            {
                Product? product = products.FirstOrDefault(
                    product => product.Id == productId);

                if (product is null || product.StockQuantity < qty)
                    return products;

                product.StockQuantity -= qty;
                reserved = true;

                return products;
            },
            ct);

        if (!reserved)
        {
            Log.Error(
                Source,
                $"Cannot reserve {qty} unit(s) of Product #{productId}.");

            return false;
        }

        await SimulatedLatency.CommitAsync(Source, ct);

        Log.Success(
            Source,
            $"Reserved {qty} unit(s) of Product #{productId}.");

        return true;
    }
    public async Task<bool> ReleaseStockAsync(
        int productId,
        int qty,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);

        await SimulatedLatency.DatabaseWriteAsync(
            Source,
            $"Release {qty} unit(s) of Product #{productId}",
            ct);

        bool released = false;

        await _store.UpdateAsync<Product>(
            DataFiles.Products,
            products =>
            {
                Product? product = products.FirstOrDefault(
                    item => item.Id == productId);

                if (product is null)
                    return products;

                product.StockQuantity = checked(
                    product.StockQuantity + qty);

                released = true;
                return products;
            },
            ct);

        if (!released)
        {
            Log.Error(
                Source,
                $"Cannot release stock for Product #{productId}.");

            return false;
        }

        await SimulatedLatency.CommitAsync(Source, ct);

        Log.Success(
            Source,
            $"Released {qty} unit(s) of Product #{productId}.");

        return true;
    }
}
