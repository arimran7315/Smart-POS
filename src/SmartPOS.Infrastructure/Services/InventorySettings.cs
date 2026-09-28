using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

internal static class InventorySettings
{
    public static async Task<decimal> GetLowStockThresholdAsync(PosDbContext db, CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking().Where(x => x.Key == "LowStockThreshold")
            .Select(x => x.Value).SingleOrDefaultAsync(ct);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) && threshold >= 0
            ? threshold
            : 0;
    }

    public static decimal EffectiveMinimum(decimal productMinimum, decimal defaultThreshold) =>
        productMinimum > 0 ? productMinimum : defaultThreshold;
}
