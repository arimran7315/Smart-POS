using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.Infrastructure.Services;

internal static class ServiceAuthorization
{
    public static async Task RequireAsync(PosDbContext db, int userId, string permission, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, permission)) throw new UnauthorizedAccessException($"Permission required: {permission}.");
    }
}

public sealed class AuthenticationService(PosDbContext db, PasswordHasher hasher)
{
    public async Task<AppUser?> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserName == userName && x.IsActive, ct);
        if (user is null || !hasher.Verify(password, user.PasswordHash)) return null;
        db.AuditEntries.Add(new AuditEntry { UserId = user.Id, Action = "SignIn", EntityName = "Authentication", EntityId = user.Id.ToString(), Description = "User signed in." });
        await db.SaveChangesAsync(ct); return user;
    }
    public async Task<AppUser> CreateFirstAdministratorAsync(string username, string displayName, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) throw new InvalidOperationException("An administrator already exists.");
        ArgumentException.ThrowIfNullOrWhiteSpace(username); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var admin = new AppUser { UserName = username.Trim(), DisplayName = displayName.Trim(), Role = "Administrator", PasswordHash = hasher.Hash(password) };
        db.Users.Add(admin); await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = admin.Id, Action = "Create", EntityName = "User", EntityId = admin.Id.ToString(), Description = "Created first administrator account." });
        await db.SaveChangesAsync(ct); return admin;
    }
}

public sealed class SaleService(PosDbContext db)
{
    public async Task<List<Sale>> SearchAsync(int userId, string invoiceFilter, int take = 300, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.View", ct);
        take = Math.Clamp(take, 1, 500);
        IQueryable<Sale> query = db.Sales.AsNoTracking().Include(x => x.Items).OrderByDescending(x => x.SoldAtUtc);
        var key = invoiceFilter.Trim(); if (key.Length > 0) query = query.Where(x => x.InvoiceNumber.Contains(key));
        return await query.Take(take).ToListAsync(ct);
    }

    public async Task<Sale> CompleteAsync(IReadOnlyCollection<(int ProductId, decimal Quantity)> cart, int userId,
        int? customerId, decimal billDiscount, decimal paid, string paymentMethod, string? notes, CancellationToken ct = default)
    {
        if (cart.Count == 0) throw new InvalidOperationException("The cart is empty.");
        if (cart.Any(x => x.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(cart), "Quantities must be positive.");
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        var grouped = cart.GroupBy(x => x.ProductId).Select(g => (Id: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var products = await db.Products.Where(p => grouped.Select(x => x.Id).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != grouped.Length) throw new InvalidOperationException("One or more products no longer exist.");
        Customer? selectedCustomer = null;
        if (customerId is not null)
            selectedCustomer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
                ?? throw new InvalidOperationException("Customer is not active or does not exist.");
        foreach (var item in grouped)
        {
            var product = products[item.Id];
            if (!product.IsActive) throw new InvalidOperationException($"{product.Name} is inactive.");
            if (product.CurrentStock < item.Quantity) throw new InvalidOperationException($"Insufficient stock for {product.Name}. Available: {product.CurrentStock}.");
        }
        var lines = grouped.Select(x => { var p = products[x.Id]; return new SaleLine(x.Quantity, p.SalePrice, p.PurchasePrice, p.DiscountRate, p.TaxRate); }).ToArray();
        var totals = SaleCalculator.Calculate(lines, billDiscount, paid);
        if (paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase))
        {
            if (selectedCustomer is null) throw new InvalidOperationException("Select a customer before completing a credit sale.");
            if (selectedCustomer.CreditLimit <= 0) throw new InvalidOperationException("Set a positive credit limit for this customer before making a credit sale.");
            var currentBalances = await db.Sales.Where(s => s.CustomerId == selectedCustomer.Id && s.Status != "Voided" && s.RemainingAmount > 0).Select(s => s.RemainingAmount).ToListAsync(ct);
            if (currentBalances.Sum() + Math.Max(0, totals.GrandTotal - paid) > selectedCustomer.CreditLimit)
                throw new InvalidOperationException("This sale exceeds the customer's available credit limit.");
        }
        if (paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase) && customerId is null)
            throw new InvalidOperationException("Select a customer before completing a credit sale.");
        if (paid > totals.GrandTotal && !paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Overpayment is only accepted for cash sales.");
        if (paid < totals.GrandTotal && !paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only credit sales can have an outstanding balance.");
        var now = DateTime.UtcNow;
        var prefix = await db.Settings.Where(x => x.Key == "InvoicePrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "INV";
        var invoice = $"{prefix}-{now:yyyy}-{await db.Sales.CountAsync(s => s.SoldAtUtc.Year == now.Year, ct) + 1:000000}";
        var sale = new Sale { InvoiceNumber = invoice, UserId = userId, CustomerId = customerId, SoldAtUtc = now, Subtotal = totals.Subtotal,
            Discount = totals.Discount, Tax = totals.Tax, GrandTotal = totals.GrandTotal, PaidAmount = Math.Min(paid, totals.GrandTotal),
            RemainingAmount = Math.Max(0, totals.GrandTotal - paid), CashReceivedAmount = paid, PaymentMethod = paymentMethod, Status = paid < totals.GrandTotal ? "Credit" : "Completed", Notes = notes };
        foreach (var item in grouped)
        {
            var p = products[item.Id]; p.CurrentStock -= item.Quantity; p.UpdatedAtUtc = now;
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = item.Quantity, UnitPrice = p.SalePrice,
                UnitCost = p.PurchasePrice, Discount = decimal.Round(item.Quantity * p.SalePrice * p.DiscountRate / 100m, 2),
                Tax = decimal.Round(item.Quantity * p.SalePrice * (1 - p.DiscountRate / 100m) * p.TaxRate / 100m, 2),
                Total = decimal.Round(item.Quantity * p.SalePrice * (1 - p.DiscountRate / 100m) * (1 + p.TaxRate / 100m), 2) });
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = -item.Quantity, BalanceAfter = p.CurrentStock, Reason = "Sale", Reference = invoice, UserId = userId });
        }
        if (sale.PaidAmount > 0) sale.Payments.Add(new Payment { Amount = sale.PaidAmount, Method = paymentMethod });
        db.Sales.Add(sale); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Create", EntityName = "Sale", EntityId = invoice, Description = $"Completed sale {invoice}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return sale;
    }
}

public sealed class PurchaseService(PosDbContext db)
{
    public async Task<Purchase> ReceiveAsync(int supplierId, int userId, IReadOnlyCollection<(int ProductId, decimal Quantity, decimal UnitCost)> lines, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Manage", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0 || x.UnitCost < 0)) throw new ArgumentException("Purchase requires items with positive quantities and non-negative costs.", nameof(lines));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var grouped = lines.GroupBy(x => (x.ProductId, x.UnitCost)).Select(g => (g.Key.ProductId, g.Sum(x => x.Quantity), g.Key.UnitCost)).ToArray();
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct)) throw new InvalidOperationException("Supplier is not active or does not exist.");
        var products = await db.Products.Where(p => grouped.Select(x => x.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != grouped.Select(x => x.ProductId).Distinct().Count()) throw new InvalidOperationException("A purchase product was not found.");
        var now = DateTime.UtcNow; var prefix = await db.Settings.Where(x => x.Key == "PurchasePrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "PUR";
        var invoice = $"{prefix}-{now:yyyy}-{await db.Purchases.CountAsync(p => p.PurchasedAtUtc.Year == now.Year, ct) + 1:000000}";
        var purchase = new Purchase { InvoiceNumber = invoice, SupplierId = supplierId, UserId = userId, PurchasedAtUtc = now };
        foreach (var item in grouped)
        {
            var p = products[item.ProductId]; p.CurrentStock += item.Item2; p.PurchasePrice = item.UnitCost; p.UpdatedAtUtc = now;
            purchase.Items.Add(new PurchaseItem { ProductId = p.Id, Quantity = item.Item2, UnitCost = item.UnitCost, Total = item.Item2 * item.UnitCost });
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = item.Item2, BalanceAfter = p.CurrentStock, Reason = "Purchase", Reference = invoice, UserId = userId });
        }
        purchase.Total = purchase.Items.Sum(x => x.Total); db.Purchases.Add(purchase);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Receive", EntityName = "Purchase", EntityId = invoice, Description = $"Received purchase {invoice}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return purchase;
    }
}

public sealed class ReturnsService(PosDbContext db)
{
    public async Task<SaleReturn> ReturnAsync(int saleId, int userId, IReadOnlyCollection<(int SaleItemId, decimal Quantity)> lines, string reason, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Return", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0)) throw new ArgumentException("Return requires positive item quantities.", nameof(lines));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId, ct);
        var saleItemIds = sale.Items.Select(i => i.Id).ToArray();
        var already = await db.ReturnItems.Where(r => saleItemIds.Contains(r.SaleItemId)).GroupBy(r => r.SaleItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        var now = DateTime.UtcNow; var number = $"RET-{now:yyyy}-{await db.Returns.CountAsync(r => r.ReturnedAtUtc.Year == now.Year, ct) + 1:000000}";
        var ret = new SaleReturn { ReturnNumber = number, SaleId = saleId, UserId = userId, ReturnedAtUtc = now, Reason = reason };
        var requested = lines.GroupBy(x => x.SaleItemId).Select(g => (SaleItemId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        foreach (var request in requested)
        {
            var item = sale.Items.SingleOrDefault(i => i.Id == request.SaleItemId) ?? throw new InvalidOperationException("Return item is not part of this sale.");
            if (request.Quantity + already.GetValueOrDefault(item.Id) > item.Quantity) throw new InvalidOperationException($"Return quantity exceeds quantity sold for {item.ProductName}.");
            var product = await db.Products.SingleAsync(p => p.Id == item.ProductId, ct); product.CurrentStock += request.Quantity;
            var billDiscount = Math.Max(0, sale.Discount - sale.Items.Sum(i => i.Discount));
            var lineShare = sale.Items.Sum(i => i.Total) == 0 ? 0 : billDiscount * item.Total / sale.Items.Sum(i => i.Total);
            var unitRefund = (item.Total - lineShare) / item.Quantity; var refund = decimal.Round(unitRefund * request.Quantity, 2);
            ret.Items.Add(new SaleReturnItem { SaleItemId = item.Id, ProductId = item.ProductId, Quantity = request.Quantity, Refund = refund }); ret.Total += refund;
            db.StockMovements.Add(new StockMovement { ProductId = item.ProductId, QuantityDelta = request.Quantity, BalanceAfter = product.CurrentStock, Reason = "Return", Reference = number, UserId = userId });
        }
        var outstandingBeforeReturn = sale.RemainingAmount;
        var cashRefund = Math.Max(0, ret.Total - outstandingBeforeReturn);
        if (cashRefund > sale.PaidAmount) throw new InvalidOperationException("Refund exceeds the net amount paid on this sale.");
        sale.RemainingAmount = Math.Max(0, outstandingBeforeReturn - ret.Total);
        sale.PaidAmount -= cashRefund;
        if (cashRefund > 0) db.Payments.Add(new Payment { SaleId = sale.Id, Amount = -cashRefund, Method = "Refund", Reference = number });
        var returnedItems = already.ToDictionary(x => x.Key, x => x.Value);
        foreach (var item in ret.Items) returnedItems[item.SaleItemId] = returnedItems.GetValueOrDefault(item.SaleItemId) + item.Quantity;
        sale.Status = sale.Items.All(item => returnedItems.GetValueOrDefault(item.Id) >= item.Quantity) ? "Returned" : "PartiallyReturned";
        sale.UpdatedAtUtc = now;
        db.Returns.Add(ret); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Return", EntityName = "Sale", EntityId = saleId.ToString(), Description = $"Processed return {number}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return ret;
    }
}

public sealed record ReturnablePurchaseItem(int PurchaseItemId, int ProductId, string ProductName, decimal QuantityRemaining, decimal UnitCost);

public sealed class PurchaseReturnsService(PosDbContext db)
{
    public async Task<IReadOnlyList<Purchase>> SearchPurchasesAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Return", ct);
        return await db.Purchases.AsNoTracking().Include(x => x.Items)
            .Where(x => x.Status != "Returned").OrderByDescending(x => x.PurchasedAtUtc).Take(200).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReturnablePurchaseItem>> GetReturnableItemsAsync(int purchaseId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Return", ct);
        var purchase = await db.Purchases.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == purchaseId, ct)
            ?? throw new InvalidOperationException("Purchase was not found.");
        var itemIds = purchase.Items.Select(x => x.Id).ToArray();
        var returned = await db.PurchaseReturnItems.AsNoTracking().Where(x => itemIds.Contains(x.PurchaseItemId))
            .GroupBy(x => x.PurchaseItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        var productIds = purchase.Items.Select(x => x.ProductId).Distinct().ToArray();
        var names = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return purchase.Items.Select(item => new ReturnablePurchaseItem(item.Id, item.ProductId,
            names.GetValueOrDefault(item.ProductId, "Unknown product"), item.Quantity - returned.GetValueOrDefault(item.Id), item.UnitCost))
            .Where(x => x.QuantityRemaining > 0).ToArray();
    }

    public async Task<PurchaseReturn> ReturnAsync(int purchaseId, int userId, IReadOnlyCollection<(int PurchaseItemId, decimal Quantity)> lines, string reason, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Return", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0)) throw new ArgumentException("Return requires positive item quantities.", nameof(lines));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A return reason is required.", nameof(reason));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var purchase = await db.Purchases.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == purchaseId, ct)
            ?? throw new InvalidOperationException("Purchase was not found.");
        var requested = lines.GroupBy(x => x.PurchaseItemId).Select(g => (PurchaseItemId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        if (requested.Any(x => purchase.Items.All(i => i.Id != x.PurchaseItemId))) throw new InvalidOperationException("Return item is not part of this purchase.");
        var itemIds = purchase.Items.Select(x => x.Id).ToArray();
        var already = await db.PurchaseReturnItems.Where(x => itemIds.Contains(x.PurchaseItemId)).GroupBy(x => x.PurchaseItemId)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        var now = DateTime.UtcNow;
        var prefix = await db.Settings.Where(x => x.Key == "PurchaseReturnPrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "PRT";
        var number = $"{prefix}-{now:yyyy}-{await db.PurchaseReturns.CountAsync(x => x.ReturnedAtUtc.Year == now.Year, ct) + 1:000000}";
        var result = new PurchaseReturn { ReturnNumber = number, PurchaseId = purchaseId, UserId = userId, ReturnedAtUtc = now, Reason = reason.Trim() };
        foreach (var request in requested)
        {
            var item = purchase.Items.Single(x => x.Id == request.PurchaseItemId);
            if (request.Quantity + already.GetValueOrDefault(item.Id) > item.Quantity)
                throw new InvalidOperationException("Return quantity exceeds the unreturned quantity received.");
            var product = await db.Products.SingleAsync(x => x.Id == item.ProductId, ct);
            if (request.Quantity > product.CurrentStock)
                throw new InvalidOperationException($"Cannot return {request.Quantity} units of {product.Name}; only {product.CurrentStock} are in stock.");
            product.CurrentStock -= request.Quantity;
            product.UpdatedAtUtc = now;
            var total = decimal.Round(request.Quantity * item.UnitCost, 2, MidpointRounding.AwayFromZero);
            result.Items.Add(new PurchaseReturnItem { PurchaseItemId = item.Id, ProductId = product.Id, Quantity = request.Quantity, Total = total });
            result.Total += total;
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, QuantityDelta = -request.Quantity, BalanceAfter = product.CurrentStock, Reason = "PurchaseReturn", Reference = number, UserId = userId });
        }
        var allReturned = await db.PurchaseReturnItems.Where(x => itemIds.Contains(x.PurchaseItemId)).Select(x => new { x.PurchaseItemId, x.Quantity }).ToListAsync(ct);
        var returnedQuantities = allReturned.GroupBy(x => x.PurchaseItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var item in result.Items) returnedQuantities[item.PurchaseItemId] = returnedQuantities.GetValueOrDefault(item.PurchaseItemId) + item.Quantity;
        purchase.Status = purchase.Items.All(item => returnedQuantities.GetValueOrDefault(item.Id) >= item.Quantity) ? "Returned" : "PartiallyReturned";
        purchase.UpdatedAtUtc = now;
        db.PurchaseReturns.Add(result);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Return", EntityName = "Purchase", EntityId = purchaseId.ToString(), Description = $"Processed purchase return {number} for {result.Total:N2}. Reason: {result.Reason}" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}

public sealed class InventoryService(PosDbContext db)
{
    public async Task AdjustAsync(int productId, decimal delta, int userId, string reason, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Inventory.Manage", ct);
        if (delta == 0) throw new ArgumentOutOfRangeException(nameof(delta));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.Products.SingleAsync(x => x.Id == productId, ct);
        if (p.CurrentStock + delta < 0) throw new InvalidOperationException("Stock cannot become negative.");
        p.CurrentStock += delta; p.UpdatedAtUtc = DateTime.UtcNow;
        db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = delta, BalanceAfter = p.CurrentStock, Reason = reason, UserId = userId });
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Adjust", EntityName = "Product", EntityId = p.Id.ToString(), Description = $"Stock adjustment {delta}: {reason}" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}

public sealed record DashboardSnapshot(decimal TodaySales, decimal TodayPurchases, decimal? TodayProfit, decimal TodayExpenses,
    int ProductCount, int LowStockCount, int CustomerCount, int SupplierCount);

public sealed class DashboardService(PosDbContext db)
{
    private sealed record ReturnProfitLine(decimal Refund, decimal Quantity, decimal SoldQuantity, decimal Tax, decimal UnitCost);
    public async Task<DashboardSnapshot> GetTodayAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new UnauthorizedAccessException("An active user session is required.");
        var canViewProfit = Permissions.Has(user.Role, "Reports.Profit");
        var localStart = DateTime.Today;
        var start = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);
        var end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), TimeZoneInfo.Local);
        var sales = db.Sales.Where(x => x.SoldAtUtc >= start && x.SoldAtUtc < end && x.Status != "Voided");
        var purchases = db.Purchases.Where(x => x.PurchasedAtUtc >= start && x.PurchasedAtUtc < end && x.Status != "Voided");
        var expenses = db.Expenses.Where(x => x.SpentAtUtc >= start && x.SpentAtUtc < end);
        var saleRows = await sales.Select(x => new { x.Id, x.GrandTotal, x.Discount }).ToListAsync(ct);
        var returnedToday = await db.Returns.AsNoTracking().Where(x => x.ReturnedAtUtc >= start && x.ReturnedAtUtc < end).Select(x => new { x.Id, x.Total }).ToListAsync(ct);
        var saleIds = saleRows.Select(x => x.Id).ToArray();
        var saleItems = canViewProfit ? await db.SaleItems.Where(x => saleIds.Contains(x.SaleId)).Select(x => new { x.SaleId, x.Total, x.Tax, x.UnitCost, x.Quantity, x.Discount }).ToListAsync(ct) : null;
        var returnedItems = new List<ReturnProfitLine>();
        if (canViewProfit && returnedToday.Count > 0)
        {
            var returnIds = returnedToday.Select(x => x.Id).ToArray();
            returnedItems = await (from returned in db.ReturnItems.AsNoTracking()
                join item in db.SaleItems.AsNoTracking() on returned.SaleItemId equals item.Id
                where returnIds.Contains(returned.SaleReturnId)
                select new ReturnProfitLine(returned.Refund, returned.Quantity, item.Quantity, item.Tax, item.UnitCost)).ToListAsync(ct);
        }
        var purchaseTotals = await purchases.Select(x => x.Total).ToListAsync(ct);
        var expenseTotals = await expenses.Select(x => x.Amount).ToListAsync(ct);
        var returnProfit = returnedItems.Sum(x => x.Refund - x.Tax * x.Quantity / x.SoldQuantity - x.UnitCost * x.Quantity);
        return new DashboardSnapshot(saleRows.Sum(x => x.GrandTotal) - returnedToday.Sum(x => x.Total), purchaseTotals.Sum(),
            saleItems is null ? null : saleItems.Sum(x => x.Total - x.Tax - x.UnitCost * x.Quantity)
                - saleRows.Sum(s => s.Discount - saleItems.Where(i => i.SaleId == s.Id).Sum(i => i.Discount)) - returnProfit, expenseTotals.Sum(),
            await db.Products.CountAsync(x => x.IsActive, ct), await db.Products.CountAsync(x => x.IsActive && x.CurrentStock <= x.MinimumStock, ct),
            await db.Customers.CountAsync(x => x.IsActive, ct), await db.Suppliers.CountAsync(x => x.IsActive, ct));
    }
}

public sealed class ProductService(PosDbContext db)
{
    public async Task<Product> SaveAsync(Product product, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(product.Name); ArgumentException.ThrowIfNullOrWhiteSpace(product.Sku);
        if (product.PurchasePrice < 0 || product.SalePrice < 0 || product.MinimumStock < 0 || product.CurrentStock < 0) throw new ArgumentOutOfRangeException(nameof(product));
        if (product.TaxRate is < 0 or > 100 || product.DiscountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(product));
        var duplicate = await db.Products.AnyAsync(p => p.Id != product.Id && (p.Sku == product.Sku || (product.Barcode != null && p.Barcode == product.Barcode)), ct);
        if (duplicate) throw new InvalidOperationException("SKU or barcode is already assigned to another product.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var isNew = product.Id == 0;
        if (isNew) db.Products.Add(product);
        else
        {
            var existing = await db.Products.SingleAsync(x => x.Id == product.Id, ct);
            db.Entry(existing).CurrentValues.SetValues(product);
            existing.UpdatedAtUtc = DateTime.UtcNow;
            product = existing;
        }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = isNew ? "Create" : "Update", EntityName = "Product", EntityId = product.Id.ToString(), Description = $"{(isNew ? "Created" : "Updated")} product {product.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return product;
    }
    public async Task DeactivateAsync(int productId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        var product = await db.Products.SingleAsync(x => x.Id == productId, ct); product.IsActive = false; product.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Deactivate", EntityName = "Product", EntityId = productId.ToString(), Description = $"Deactivated product {product.Name}." });
        await db.SaveChangesAsync(ct);
    }
    public async Task<List<Product>> SearchAsync(string query, int userId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.View", ct);
        take = Math.Clamp(take, 1, 200);
        return await db.Products.AsNoTracking().Where(p => p.IsActive && (query == "" || p.Name.Contains(query) || p.Sku.Contains(query) || p.Barcode == query))
            .OrderBy(p => p.Name).Skip(Math.Max(0, skip)).Take(take).ToListAsync(ct);
    }
}
