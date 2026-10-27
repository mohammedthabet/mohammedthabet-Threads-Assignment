using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public sealed class RaceConditionDemo
{
    private readonly JsonFileStore _store;
    private readonly ProductRepository _products;

    public RaceConditionDemo(
        JsonFileStore store,
        ProductRepository products)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(products);

        _store = store;
        _products = products;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        const int productId = 4;
        const int quantity = 1;

        Console.WriteLine(
            "\n========== RACE CONDITION DEMO ==========");

        await SetStockAsync(productId, 1, ct);

        bool[] unsafeResults = await Task.WhenAll(
            ReserveStockUnsafeAsync(productId, quantity, ct),
            ReserveStockUnsafeAsync(productId, quantity, ct));

        Product? unsafeProduct =
            await _products.GetByIdAsync(productId, ct);

        Console.WriteLine("\n--- Unsafe reservation ---");
        Console.WriteLine(
            $"Successful reservations: {unsafeResults.Count(x => x)}");
        Console.WriteLine(
            $"Final stock: {unsafeProduct?.StockQuantity}");

        await SetStockAsync(productId, 1, ct);

        bool[] safeResults = await Task.WhenAll(
            _products.ReserveStockAsync(productId, quantity, ct),
            _products.ReserveStockAsync(productId, quantity, ct));

        Product? safeProduct =
            await _products.GetByIdAsync(productId, ct);

        Console.WriteLine("\n--- Safe reservation ---");
        Console.WriteLine(
            $"Successful reservations: {safeResults.Count(x => x)}");
        Console.WriteLine(
            $"Final stock: {safeProduct?.StockQuantity}");

        if (unsafeResults.Count(x => x) != 2 ||
            unsafeProduct?.StockQuantity != 0)
        {
            throw new InvalidOperationException(
                "The unsafe race condition was not reproduced.");
        }

        if (safeResults.Count(x => x) != 1 ||
            safeProduct?.StockQuantity != 0)
        {
            throw new InvalidOperationException(
                "Safe reservation did not preserve stock consistency.");
        }

        Log.Success(
            "RaceConditionDemo",
            "PASS: Unsafe overselling reproduced; safe reservation prevented it.");
    }

    private async Task<bool> ReserveStockUnsafeAsync(
        int productId,
        int quantity,
        CancellationToken ct)
    {
        List<Product> products =
            await _store.ReadAllAsync<Product>(DataFiles.Products, ct);

        Product? product = products.FirstOrDefault(
            item => item.Id == productId);

        if (product is null || product.StockQuantity < quantity)
            return false;

        // Deliberately unsafe: stock is read before the simulated write.
        int remainingStock = product.StockQuantity - quantity;

        await SimulatedLatency.DatabaseWriteAsync(
            "RaceConditionDemo",
            $"Unsafe reservation for Product #{productId}",
            ct);

        await _store.UpdateAsync<Product>(
            DataFiles.Products,
            current =>
            {
                Product? target = current.FirstOrDefault(
                    item => item.Id == productId);

                if (target is not null)
                    target.StockQuantity = remainingStock;

                return current;
            },
            ct);

        await SimulatedLatency.CommitAsync(
            "RaceConditionDemo", ct);

        return true;
    }

    private Task<List<Product>> SetStockAsync(
        int productId,
        int quantity,
        CancellationToken ct)
    {
        return _store.UpdateAsync<Product>(
            DataFiles.Products,
            products =>
            {
                Product? product = products.FirstOrDefault(
                    item => item.Id == productId);

                if (product is null)
                {
                    throw new InvalidOperationException(
                        $"Product #{productId} was not found.");
                }

                product.StockQuantity = quantity;
                return products;
            },
            ct);
    }
}
