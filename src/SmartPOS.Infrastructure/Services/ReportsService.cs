using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

public sealed record DailySalesPoint(DateOnly Day, decimal SalesTotal, int SaleCount);
public sealed record TopProductRow(int ProductId, string ProductName, decimal UnitsSold, decimal Revenue);
public sealed record DailyProfitPoint(DateOnly Day, decimal Profit);

public sealed class ReportsService(PosDbContext db)
{
    public async Task<IReadOnlyList<DailySalesPoint>> GetDailySalesAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var rows = await db.Sales.AsNoTracking().Where(s => s.SoldAtUtc >= start && s.SoldAtUtc < end && s.Status != "Voided")
            .Select(s => new { s.SoldAtUtc, s.GrandTotal }).ToListAsync(ct);
        var returns = await db.Returns.AsNoTracking().Where(r => r.ReturnedAtUtc >= start && r.ReturnedAtUtc < end)
            .Select(r => new { r.ReturnedAtUtc, r.Total }).ToListAsync(ct);
        var salesByDay = rows.GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(x.SoldAtUtc, TimeZoneInfo.Local)))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.GrandTotal), Count: g.Count()));
        var returnsByDay = returns.GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(x.ReturnedAtUtc, TimeZoneInfo.Local)))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
        return salesByDay.Keys.Union(returnsByDay.Keys).OrderBy(x => x).Select(day => new DailySalesPoint(day,
            salesByDay.GetValueOrDefault(day).Total - returnsByDay.GetValueOrDefault(day), salesByDay.GetValueOrDefault(day).Count)).ToArray();
    }

    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(int userId, DateOnly from, DateOnly through, int take = 10, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through); take = Math.Clamp(take, 1, 100);
        var rows = await (from item in db.SaleItems.AsNoTracking()
                          join sale in db.Sales.AsNoTracking() on item.SaleId equals sale.Id
                          where sale.SoldAtUtc >= start && sale.SoldAtUtc < end && sale.Status != "Voided"
                          select new { item.ProductId, item.ProductName, item.Quantity, item.Total }).ToListAsync(ct);
        var returns = await (from returned in db.ReturnItems.AsNoTracking()
                             join ret in db.Returns.AsNoTracking() on returned.SaleReturnId equals ret.Id
                             where ret.ReturnedAtUtc >= start && ret.ReturnedAtUtc < end
                             select new { returned.ProductId, returned.Quantity, returned.Refund }).ToListAsync(ct);
        return rows.GroupBy(x => new { x.ProductId, x.ProductName }).Select(g =>
        {
            var returned = returns.Where(x => x.ProductId == g.Key.ProductId).ToArray();
            return new TopProductRow(g.Key.ProductId, g.Key.ProductName, g.Sum(x => x.Quantity) - returned.Sum(x => x.Quantity),
                g.Sum(x => x.Total) - returned.Sum(x => x.Refund));
        }).Where(x => x.UnitsSold > 0).OrderByDescending(x => x.UnitsSold).ThenBy(x => x.ProductName).Take(take).ToArray();
    }

    public async Task<IReadOnlyList<DailyProfitPoint>> GetDailyProfitAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.Profit", ct);
        var (start, end) = Range(from, through);
        var sales = await db.Sales.AsNoTracking().Where(s => s.SoldAtUtc >= start && s.SoldAtUtc < end && s.Status != "Voided")
            .Select(s => new { s.Id, s.SoldAtUtc, s.Discount }).ToListAsync(ct);
        var ids = sales.Select(s => s.Id).ToArray();
        var items = await db.SaleItems.AsNoTracking().Where(i => ids.Contains(i.SaleId))
            .Select(i => new { i.SaleId, i.Total, i.Tax, i.UnitCost, i.Quantity, i.Discount }).ToListAsync(ct);
        var daily = sales.GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s.SoldAtUtc, TimeZoneInfo.Local)))
            .ToDictionary(day => day.Key, day => day.Sum(s =>
            {
                var saleItems = items.Where(i => i.SaleId == s.Id).ToArray();
                var billDiscount = Math.Max(0, s.Discount - saleItems.Sum(i => i.Discount));
                return saleItems.Sum(i => i.Total - i.Tax - i.UnitCost * i.Quantity) - billDiscount;
            }));
        var returns = await db.Returns.AsNoTracking().Where(r => r.ReturnedAtUtc >= start && r.ReturnedAtUtc < end)
            .Select(r => new { r.Id, r.ReturnedAtUtc }).ToListAsync(ct);
        var returnIds = returns.Select(x => x.Id).ToArray();
        var returnedItems = await (from returned in db.ReturnItems.AsNoTracking()
            join item in db.SaleItems.AsNoTracking() on returned.SaleItemId equals item.Id
            where returnIds.Contains(returned.SaleReturnId)
            select new { returned.SaleReturnId, returned.Refund, returned.Quantity, QuantitySold = item.Quantity, item.Tax, item.UnitCost }).ToListAsync(ct);
        foreach (var group in returns.GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.ReturnedAtUtc, TimeZoneInfo.Local))))
        {
            var idSet = group.Select(x => x.Id).ToHashSet();
            var reduction = returnedItems.Where(x => idSet.Contains(x.SaleReturnId)).Sum(x => x.Refund - x.Tax * x.Quantity / x.QuantitySold - x.UnitCost * x.Quantity);
            daily[group.Key] = daily.GetValueOrDefault(group.Key) - reduction;
        }
        return daily.OrderBy(x => x.Key).Select(x => new DailyProfitPoint(x.Key, x.Value)).ToArray();
    }

    private static (DateTime StartUtc, DateTime EndUtc) Range(DateOnly from, DateOnly through)
    {
        if (through < from || through.DayNumber - from.DayNumber > 366) throw new ArgumentOutOfRangeException(nameof(through), "Choose a date range no longer than 367 calendar days.");
        var localStart = from.ToDateTime(TimeOnly.MinValue); var localEnd = through.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local), TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local));
    }
}
