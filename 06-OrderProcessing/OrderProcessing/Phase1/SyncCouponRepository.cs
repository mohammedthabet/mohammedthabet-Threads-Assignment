using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class SyncCouponRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "SyncCouponRepository";

    public SyncCouponRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Coupon? GetByCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Log.Info(Source, $"Get Coupon '{code}': Opening connection...");
        Thread.Sleep(200);
        Log.Info(Source, $"Get Coupon '{code}': Executing query...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Coupon? coupon = _store.ReadAll<Coupon>(DataFiles.Coupons)
            .FirstOrDefault(item => string.Equals(
                item.Code, code, StringComparison.OrdinalIgnoreCase));

        if (coupon is null)
            Log.Error(Source, $"Coupon '{code}' not found.");
        else
            Log.Success(Source, $"Coupon '{code}' found.");

        return coupon;
    }
}
