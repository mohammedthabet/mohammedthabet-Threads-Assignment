using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public sealed class CouponRepository
{
    private readonly JsonFileStore _store;
    private const string Source = "CouponRepository";

    public CouponRepository(JsonFileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<Coupon?> GetByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        await SimulatedLatency.DatabaseQueryAsync(
            Source,
            $"Get Coupon '{code}'",
            ct);

        List<Coupon> coupons =
            await _store.ReadAllAsync<Coupon>(DataFiles.Coupons, ct);

        Coupon? coupon = coupons.FirstOrDefault(
            coupon => string.Equals(
                coupon.Code,
                code,
                StringComparison.OrdinalIgnoreCase));

        if (coupon is null)
        {
            Log.Error(Source, $"Coupon '{code}' not found.");
            return null;
        }

        Log.Success(Source, $"Coupon '{code}' found.");

        return coupon;
    }
}
