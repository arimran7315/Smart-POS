using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

public sealed record DailySalesPoint(DateOnly Day, decimal SalesTotal, int SaleCount);
public sealed record TopProductRow(int ProductId, string ProductName, decimal UnitsSold, decimal Revenue);
public sealed record DailyProfitPoint(DateOnly Day, decimal Profit);
public sealed record CashierSalesRow(string CashierName, decimal Sales, int SaleCount);
public sealed record CustomerSalesRow(string CustomerName, decimal Sales, int SaleCount);
public sealed record PurchaseDayPoint(DateOnly Day, decimal NetPurchases, int PurchaseCount);
public sealed record SupplierPurchaseRow(string SupplierName, decimal Purchases, int PurchaseCount);
public sealed record ExpenseReportRow(DateOnly Day, string Category, string PaymentMethod, decimal Amount, int Count);
public sealed record PaymentMethodRow(string Method, decimal NetAmount, int PaymentCount);
public sealed record ReturnReportRow(DateOnly Day, string Type, string ReturnNumber, string Reason, decimal Total);
public sealed record TaxDayPoint(DateOnly Day, decimal NetTax);
public sealed record StockMovementReportRow(DateTime DateUtc, string ProductName, decimal QuantityChange, decimal BalanceAfter, string Reason, string? Reference);
public sealed record InventoryValuationRow(string ProductName, string Sku, string? Category, decimal CurrentStock, string Unit,
    decimal PurchaseCost, decimal EstimatedStockValue, decimal MinimumStock, decimal MaximumStock);
public sealed record LowStockReportRow(string ProductName, string Sku, decimal CurrentStock, string Unit,
    decimal MinimumStock, decimal MaximumStock, decimal ReorderQuantity);
public sealed record CustomerLedgerRow(string CustomerName, DateTime DateUtc, string Reference, string Type, decimal Debit, decimal Credit, decimal Balance);
public sealed record SupplierLedgerRow(string SupplierName, DateTime DateUtc, string Reference, string Type, decimal Debit, decimal Credit, decimal Balance);

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
                          select new { SaleItemId = item.Id, item.ProductId, item.ProductName, item.Quantity, item.Total }).ToListAsync(ct);
        var returns = await (from returned in db.ReturnItems.AsNoTracking()
                             join ret in db.Returns.AsNoTracking() on returned.SaleReturnId equals ret.Id
                             where ret.ReturnedAtUtc >= start && ret.ReturnedAtUtc < end
                             select new { returned.SaleItemId, returned.Quantity, returned.Refund }).ToListAsync(ct);
        return rows.GroupBy(x => new { x.ProductId, x.ProductName }).Select(g =>
        {
            var saleItemIds = g.Select(x => x.SaleItemId).ToHashSet();
            var returned = returns.Where(x => saleItemIds.Contains(x.SaleItemId)).ToArray();
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

    public async Task<IReadOnlyList<CashierSalesRow>> GetSalesByCashierAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var sales = await (from sale in db.Sales.AsNoTracking()
                           join user in db.Users.AsNoTracking() on sale.UserId equals user.Id
                           where sale.SoldAtUtc >= start && sale.SoldAtUtc < end && sale.Status != "Voided"
                           select new { sale.UserId, user.DisplayName, sale.GrandTotal }).ToListAsync(ct);
        return sales.GroupBy(x => new { x.UserId, x.DisplayName }).Select(g => new CashierSalesRow(g.Key.DisplayName,
            g.Sum(x => x.GrandTotal), g.Count())).OrderByDescending(x => x.Sales).ToArray();
    }

    public async Task<IReadOnlyList<CustomerSalesRow>> GetSalesByCustomerAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var sales = await db.Sales.AsNoTracking().Where(x => x.SoldAtUtc >= start && x.SoldAtUtc < end && x.Status != "Voided")
            .Select(x => new { x.Id, x.CustomerId, x.GrandTotal }).ToListAsync(ct);
        var customerIds = sales.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var names = await db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return sales.GroupBy(x => x.CustomerId).Select(g => new CustomerSalesRow(g.Key is int id ? names.GetValueOrDefault(id, "Unknown customer") : "Walk-in",
            g.Sum(x => x.GrandTotal), g.Count())).OrderByDescending(x => x.Sales).ToArray();
    }

    public async Task<IReadOnlyList<PurchaseDayPoint>> GetPurchasesByDayAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var purchases = await db.Purchases.AsNoTracking().Where(x => x.PurchasedAtUtc >= start && x.PurchasedAtUtc < end && x.Status != "Voided")
            .Select(x => new { x.PurchasedAtUtc, x.Total }).ToListAsync(ct);
        var returns = await db.PurchaseReturns.AsNoTracking().Where(x => x.ReturnedAtUtc >= start && x.ReturnedAtUtc < end)
            .Select(x => new { x.ReturnedAtUtc, x.Total }).ToListAsync(ct);
        var purchaseDays = purchases.GroupBy(x => LocalDate(x.PurchasedAtUtc)).ToDictionary(g => g.Key, g => (Total: g.Sum(x => x.Total), Count: g.Count()));
        var returnedDays = returns.GroupBy(x => LocalDate(x.ReturnedAtUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
        return purchaseDays.Keys.Union(returnedDays.Keys).OrderBy(x => x).Select(day => new PurchaseDayPoint(day,
            purchaseDays.GetValueOrDefault(day).Total - returnedDays.GetValueOrDefault(day), purchaseDays.GetValueOrDefault(day).Count)).ToArray();
    }

    public async Task<IReadOnlyList<SupplierPurchaseRow>> GetPurchasesBySupplierAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var purchases = await (from purchase in db.Purchases.AsNoTracking()
                               join supplier in db.Suppliers.AsNoTracking() on purchase.SupplierId equals supplier.Id
                               where purchase.PurchasedAtUtc >= start && purchase.PurchasedAtUtc < end && purchase.Status != "Voided"
                               select new { purchase.Id, purchase.SupplierId, supplier.Name, purchase.Total }).ToListAsync(ct);
        return purchases.GroupBy(x => new { x.SupplierId, x.Name }).Select(g => new SupplierPurchaseRow(g.Key.Name, g.Sum(x => x.Total), g.Count()))
            .OrderByDescending(x => x.Purchases).ToArray();
    }

    public async Task<IReadOnlyList<ExpenseReportRow>> GetExpensesAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var rows = await db.Expenses.AsNoTracking().Where(x => x.SpentAtUtc >= start && x.SpentAtUtc < end)
            .Select(x => new { x.SpentAtUtc, x.Category, x.PaymentMethod, x.Amount }).ToListAsync(ct);
        return rows.GroupBy(x => (Day: LocalDate(x.SpentAtUtc), x.Category, x.PaymentMethod))
            .Select(g => new ExpenseReportRow(g.Key.Day, g.Key.Category, g.Key.PaymentMethod, g.Sum(x => x.Amount), g.Count()))
            .OrderBy(x => x.Day).ThenBy(x => x.Category).ThenBy(x => x.PaymentMethod).ToArray();
    }

    public async Task<IReadOnlyList<PaymentMethodRow>> GetPaymentsAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var rows = await (from payment in db.Payments.AsNoTracking()
                          join sale in db.Sales.AsNoTracking() on payment.SaleId equals sale.Id
                          where payment.PaidAtUtc >= start && payment.PaidAtUtc < end && sale.Status != "Voided"
                          select new { payment.Method, payment.Amount }).ToListAsync(ct);
        var accountReceipts = await db.CustomerAccountPayments.AsNoTracking().Where(x => x.PaidAtUtc >= start && x.PaidAtUtc < end)
            .Select(x => new { x.Method, x.Amount }).ToListAsync(ct);
        var supplierRows = await db.SupplierPayments.AsNoTracking().Where(x => x.PaidAtUtc >= start && x.PaidAtUtc < end)
            .Select(x => new { x.Method, x.Amount }).ToListAsync(ct);
        var allRows = rows.Select(x => (x.Method, x.Amount)).Concat(accountReceipts.Select(x => (x.Method, x.Amount)))
            .Concat(supplierRows.Select(x => (x.Method, Amount: -x.Amount)));
        return allRows.GroupBy(x => x.Method).Select(g => new PaymentMethodRow(g.Key, g.Sum(x => x.Amount), g.Count()))
            .OrderBy(x => x.Method).ToArray();
    }

    public async Task<IReadOnlyList<ReturnReportRow>> GetReturnsAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var saleReturns = await db.Returns.AsNoTracking().Where(x => x.ReturnedAtUtc >= start && x.ReturnedAtUtc < end)
            .Select(x => new { x.ReturnedAtUtc, Type = "Sale", x.ReturnNumber, x.Reason, x.Total }).ToListAsync(ct);
        var purchaseReturns = await db.PurchaseReturns.AsNoTracking().Where(x => x.ReturnedAtUtc >= start && x.ReturnedAtUtc < end)
            .Select(x => new { x.ReturnedAtUtc, Type = "Purchase", x.ReturnNumber, x.Reason, x.Total }).ToListAsync(ct);
        return saleReturns.Concat(purchaseReturns).Select(x => new ReturnReportRow(LocalDate(x.ReturnedAtUtc), x.Type, x.ReturnNumber, x.Reason, x.Total))
            .OrderByDescending(x => x.Day).ThenBy(x => x.ReturnNumber).ToArray();
    }

    public async Task<IReadOnlyList<TaxDayPoint>> GetTaxByDayAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var sales = await db.Sales.AsNoTracking().Where(x => x.SoldAtUtc >= start && x.SoldAtUtc < end && x.Status != "Voided")
            .Select(x => new { x.Id, x.SoldAtUtc, x.Tax }).ToListAsync(ct);
        var returnTaxes = await (from ret in db.Returns.AsNoTracking()
                                 join returnedItem in db.ReturnItems.AsNoTracking() on ret.Id equals returnedItem.SaleReturnId
                                 join item in db.SaleItems.AsNoTracking() on returnedItem.SaleItemId equals item.Id
                                 where ret.ReturnedAtUtc >= start && ret.ReturnedAtUtc < end
                                 select new { ret.ReturnedAtUtc, Tax = item.Tax * returnedItem.Quantity / item.Quantity }).ToListAsync(ct);
        var byDay = sales.GroupBy(x => LocalDate(x.SoldAtUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Tax));
        foreach (var group in returnTaxes.GroupBy(x => LocalDate(x.ReturnedAtUtc))) byDay[group.Key] = byDay.GetValueOrDefault(group.Key) - group.Sum(x => x.Tax);
        return byDay.OrderBy(x => x.Key).Select(x => new TaxDayPoint(x.Key, x.Value)).ToArray();
    }

    public async Task<IReadOnlyList<StockMovementReportRow>> GetStockMovementsAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var rows = await (from movement in db.StockMovements.AsNoTracking()
                          join product in db.Products.AsNoTracking() on movement.ProductId equals product.Id
                          where movement.CreatedAtUtc >= start && movement.CreatedAtUtc < end
                          orderby movement.CreatedAtUtc descending
                          select new StockMovementReportRow(movement.CreatedAtUtc, product.Name, movement.QuantityDelta, movement.BalanceAfter, movement.Reason, movement.Reference))
            .Take(5000).ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<InventoryValuationRow>> GetInventoryValuationAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new { p.Name, p.Sku, Category = p.Category == null ? null : p.Category.Name,
                p.CurrentStock, p.Unit, p.PurchasePrice, p.MinimumStock, p.MaximumStock }).ToListAsync(ct);
        return products.Select(p => new InventoryValuationRow(p.Name, p.Sku, p.Category,
            p.CurrentStock, p.Unit, p.PurchasePrice, p.CurrentStock * p.PurchasePrice, p.MinimumStock, p.MaximumStock)).ToArray();
    }

    public async Task<IReadOnlyList<LowStockReportRow>> GetLowStockAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var defaultThreshold = await InventorySettings.GetLowStockThresholdAsync(db, ct);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.Name, p.Sku, p.CurrentStock, p.Unit, p.MinimumStock, p.MaximumStock }).ToListAsync(ct);
        return products.Select(p => (Product: p, Threshold: InventorySettings.EffectiveMinimum(p.MinimumStock, defaultThreshold)))
            .Where(x => x.Product.CurrentStock <= x.Threshold).OrderBy(x => x.Product.CurrentStock).ThenBy(x => x.Product.Name)
            .Select(x => new LowStockReportRow(x.Product.Name, x.Product.Sku, x.Product.CurrentStock, x.Product.Unit,
                x.Threshold, x.Product.MaximumStock, x.Threshold - x.Product.CurrentStock)).ToArray();
    }

    public async Task<IReadOnlyList<CustomerLedgerRow>> GetCustomerLedgerAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var events = new List<(int CustomerId, DateTime Date, string Reference, string Type, decimal Debit, decimal Credit)>();
        var customers = await db.Customers.AsNoTracking().Where(x => x.IsActive || x.OpeningBalance != 0)
            .Select(x => new { x.Id, x.Name, x.OpeningBalance }).ToListAsync(ct);
        var customerIds = customers.Select(x => x.Id).ToArray();
        var broughtForward = customers.ToDictionary(x => x.Id, x => x.OpeningBalance);
        var sales = await db.Sales.AsNoTracking().Where(x => x.CustomerId != null && customerIds.Contains(x.CustomerId.Value)
                && x.SoldAtUtc < end && x.Status != "Voided")
            .Select(x => new { CustomerId = x.CustomerId!.Value, x.SoldAtUtc, x.InvoiceNumber, x.GrandTotal }).ToListAsync(ct);
        foreach (var row in sales)
            if (row.SoldAtUtc < start) broughtForward[row.CustomerId] += row.GrandTotal;
            else events.Add((row.CustomerId, row.SoldAtUtc, row.InvoiceNumber, "Sale", row.GrandTotal, 0m));
        var payments = await (from payment in db.Payments.AsNoTracking()
                              join sale in db.Sales.AsNoTracking() on payment.SaleId equals sale.Id
                              where sale.Status != "Voided" && sale.CustomerId != null && customerIds.Contains(sale.CustomerId.Value)
                                  && payment.PaidAtUtc < end
                              select new { CustomerId = sale.CustomerId!.Value, payment.PaidAtUtc, payment.Reference, payment.Method, payment.Amount, sale.InvoiceNumber }).ToListAsync(ct);
        foreach (var row in payments)
            if (row.PaidAtUtc < start) broughtForward[row.CustomerId] -= row.Amount;
            else events.Add((row.CustomerId, row.PaidAtUtc, row.Reference ?? row.InvoiceNumber, row.Method,
                Math.Max(0, -row.Amount), Math.Max(0, row.Amount)));
        var accountReceipts = await db.CustomerAccountPayments.AsNoTracking().Where(x => customerIds.Contains(x.CustomerId) && x.PaidAtUtc < end)
            .Select(x => new { x.CustomerId, x.PaidAtUtc, x.Reference, x.Id, x.Method, x.Amount }).ToListAsync(ct);
        foreach (var row in accountReceipts)
            if (row.PaidAtUtc < start) broughtForward[row.CustomerId] -= row.Amount;
            else events.Add((row.CustomerId, row.PaidAtUtc, row.Reference ?? $"CP-{row.Id}", $"Account payment ({row.Method})", 0m, row.Amount));
        var returns = await (from ret in db.Returns.AsNoTracking()
                             join sale in db.Sales.AsNoTracking() on ret.SaleId equals sale.Id
                             where sale.CustomerId != null && customerIds.Contains(sale.CustomerId.Value)
                                 && ret.ReturnedAtUtc < end
                             select new { CustomerId = sale.CustomerId!.Value, ret.ReturnedAtUtc, ret.ReturnNumber, ret.Total }).ToListAsync(ct);
        foreach (var row in returns)
            if (row.ReturnedAtUtc < start) broughtForward[row.CustomerId] -= row.Total;
            else events.Add((row.CustomerId, row.ReturnedAtUtc, row.ReturnNumber, "Sale return", 0m, row.Total));
        foreach (var customer in customers)
            if (broughtForward[customer.Id] != 0)
                events.Add((customer.Id, start, "OPENING", "Balance brought forward",
                    Math.Max(0, broughtForward[customer.Id]), Math.Max(0, -broughtForward[customer.Id])));

        var names = customers.ToDictionary(x => x.Id, x => x.Name);
        var result = new List<CustomerLedgerRow>();
        foreach (var group in events.GroupBy(x => x.CustomerId))
        {
            decimal balance = 0;
            foreach (var entry in group.OrderBy(x => x.Date).ThenBy(x => x.Reference).ThenBy(x => x.Type))
            {
                balance += entry.Debit - entry.Credit;
                result.Add(new CustomerLedgerRow(names.GetValueOrDefault(group.Key, "Unknown customer"), entry.Date,
                    entry.Reference, entry.Type, entry.Debit, entry.Credit, balance));
            }
        }
        return result.OrderBy(x => x.CustomerName).ThenBy(x => x.DateUtc).ThenBy(x => x.Reference).ToArray();
    }

    public async Task<IReadOnlyList<SupplierLedgerRow>> GetSupplierLedgerAsync(int userId, DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Reports.View", ct);
        var (start, end) = Range(from, through);
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.IsActive || x.OpeningBalance != 0)
            .Select(x => new { x.Id, x.Name, x.OpeningBalance }).ToListAsync(ct);
        var supplierIds = suppliers.Select(x => x.Id).ToArray();
        var events = new List<(int SupplierId, DateTime Date, string Reference, string Type, decimal Debit, decimal Credit)>();
        var broughtForward = suppliers.ToDictionary(x => x.Id, x => x.OpeningBalance);
        var purchases = await db.Purchases.AsNoTracking().Where(x => supplierIds.Contains(x.SupplierId)
                && x.PurchasedAtUtc < end && x.Status != "Voided")
            .Select(x => new { x.SupplierId, x.PurchasedAtUtc, x.InvoiceNumber, x.Total }).ToListAsync(ct);
        foreach (var row in purchases)
            if (row.PurchasedAtUtc < start) broughtForward[row.SupplierId] += row.Total;
            else events.Add((row.SupplierId, row.PurchasedAtUtc, row.InvoiceNumber, "Purchase", 0m, row.Total));
        var payments = await db.SupplierPayments.AsNoTracking().Where(x => supplierIds.Contains(x.SupplierId) && x.PaidAtUtc < end)
            .Select(x => new { x.SupplierId, x.PaidAtUtc, x.Reference, x.Id, x.Amount, x.Method }).ToListAsync(ct);
        foreach (var row in payments)
            if (row.PaidAtUtc < start) broughtForward[row.SupplierId] -= row.Amount;
            else events.Add((row.SupplierId, row.PaidAtUtc, row.Reference ?? $"SP-{row.Id}", $"Payment ({row.Method})", row.Amount, 0m));
        var returns = await (from ret in db.PurchaseReturns.AsNoTracking()
                             join purchase in db.Purchases.AsNoTracking() on ret.PurchaseId equals purchase.Id
                             where supplierIds.Contains(purchase.SupplierId) && ret.ReturnedAtUtc < end
                             select new { purchase.SupplierId, ret.ReturnedAtUtc, ret.ReturnNumber, ret.Total }).ToListAsync(ct);
        foreach (var row in returns)
            if (row.ReturnedAtUtc < start) broughtForward[row.SupplierId] -= row.Total;
            else events.Add((row.SupplierId, row.ReturnedAtUtc, row.ReturnNumber, "Purchase return", row.Total, 0m));
        foreach (var supplier in suppliers)
            if (broughtForward[supplier.Id] != 0)
                events.Add((supplier.Id, start, "OPENING", "Balance brought forward",
                    Math.Max(0, -broughtForward[supplier.Id]), Math.Max(0, broughtForward[supplier.Id])));

        var names = suppliers.ToDictionary(x => x.Id, x => x.Name);
        var result = new List<SupplierLedgerRow>();
        foreach (var group in events.GroupBy(x => x.SupplierId))
        {
            decimal balance = 0;
            foreach (var entry in group.OrderBy(x => x.Date).ThenBy(x => x.Reference).ThenBy(x => x.Type))
            {
                balance += entry.Credit - entry.Debit;
                result.Add(new SupplierLedgerRow(names.GetValueOrDefault(group.Key, "Unknown supplier"), entry.Date,
                    entry.Reference, entry.Type, entry.Debit, entry.Credit, balance));
            }
        }
        return result.OrderBy(x => x.SupplierName).ThenBy(x => x.DateUtc).ThenBy(x => x.Reference).ToArray();
    }

    private static (DateTime StartUtc, DateTime EndUtc) Range(DateOnly from, DateOnly through)
    {
        if (through < from || through.DayNumber - from.DayNumber > 366) throw new ArgumentOutOfRangeException(nameof(through), "Choose a date range no longer than 367 calendar days.");
        var localStart = from.ToDateTime(TimeOnly.MinValue); var localEnd = through.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local), TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local));
    }

    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local));
}
