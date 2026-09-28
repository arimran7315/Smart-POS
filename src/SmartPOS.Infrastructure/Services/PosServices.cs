using System.Text.Json;
using System.Globalization;
using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.Infrastructure.Services;

public sealed record SaleSearchPage(List<Sale> Items, int TotalCount, int PageIndex, int PageSize)
{
    public int PageCount => TotalCount == 0 ? 0 : (int)(((long)TotalCount + PageSize - 1) / PageSize);
}

internal static class ServiceAuthorization
{
    public static async Task<string> RequireAndGetRoleAsync(PosDbContext db, int userId, string permission, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, permission)) throw new UnauthorizedAccessException("An active user with the required permission is required.");
        return user.Role;
    }
    public static async Task RequireAsync(PosDbContext db, int userId, string permission, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null || !Permissions.Has(user.Role, permission)) throw new UnauthorizedAccessException($"Permission required: {permission}.");
    }
    public static async Task RequireAnyAsync(PosDbContext db, int userId, CancellationToken ct, params string[] permissions)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null || !permissions.Any(permission => Permissions.Has(user.Role, permission)))
            throw new UnauthorizedAccessException("An active user with the required permission is required.");
    }
}

public sealed class AuthenticationService(PosDbContext db, PasswordHasher hasher)
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LoginLockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<bool> IsFirstAdministratorRequiredAsync(CancellationToken ct = default) =>
        !await db.Users.AsNoTracking().AnyAsync(ct);

    public async Task<AppUser?> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserName == userName.Trim() && x.IsActive, ct);
        if (user is null) { await tx.CommitAsync(ct); return null; }
        var now = DateTime.UtcNow;
        if (user.LockoutUntilUtc is DateTime lockoutUntil)
        {
            if (lockoutUntil > now) { await tx.CommitAsync(ct); return null; }
            user.FailedLoginAttempts = 0;
            user.LockoutUntilUtc = null;
        }
        if (!hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                user.LockoutUntilUtc = now.Add(LoginLockoutDuration);
            user.UpdatedAtUtc = now;
            db.AuditEntries.Add(new AuditEntry { UserId = user.Id, Action = "FailedSignIn", EntityName = "Authentication",
                EntityId = user.Id.ToString(), Description = $"Failed sign-in attempt {user.FailedLoginAttempts}; lockout is active: {user.LockoutUntilUtc is not null}." });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }
        user.FailedLoginAttempts = 0;
        user.LockoutUntilUtc = null;
        user.UpdatedAtUtc = now;
        db.AuditEntries.Add(new AuditEntry { UserId = user.Id, Action = "SignIn", EntityName = "Authentication", EntityId = user.Id.ToString(), Description = "User signed in." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return user;
    }
    public async Task<AppUser> CreateFirstAdministratorAsync(string username, string displayName, string password, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Users.AnyAsync(ct)) throw new PosRuleException("An administrator already exists.");
        ArgumentException.ThrowIfNullOrWhiteSpace(username); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var admin = new AppUser { UserName = username.Trim(), DisplayName = displayName.Trim(), Role = "Administrator", PasswordHash = hasher.Hash(password) };
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = admin.Id, Action = "Create", EntityName = "User", EntityId = admin.Id.ToString(), Description = "Created first administrator account." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return admin;
    }
}

public sealed class SaleService(PosDbContext db)
{
    public async Task<SaleReceiptData> GetReceiptForReprintAsync(int saleId, int userId, CancellationToken ct = default)
    {
        var sale = await GetVisibleReceiptSaleAsync(saleId, userId, ct);
        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var cashierName = await db.Users.AsNoTracking().Where(x => x.Id == sale.UserId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct)
            ?? "Unknown cashier";
        var customerName = sale.CustomerId is int customerId
            ? await db.Customers.AsNoTracking().Where(x => x.Id == customerId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            : null;
        return new SaleReceiptData(sale, settings, cashierName, customerName);
    }

    public Task RecordReceiptReprintedAsync(int saleId, int userId, CancellationToken ct = default) =>
        RecordReceiptPrintedAsync(saleId, userId, reprint: true, ct: ct);

    public async Task RecordReceiptPrintedAsync(int saleId, int userId, bool reprint = false, CancellationToken ct = default)
    {
        var sale = await GetVisibleReceiptSaleAsync(saleId, userId, ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Print", EntityName = "Sale",
            EntityId = sale.InvoiceNumber, Description = $"{(reprint ? "Reprinted" : "Printed")} receipt for {sale.InvoiceNumber}." });
        await db.SaveChangesAsync(ct);
    }

    private async Task<Sale> GetVisibleReceiptSaleAsync(int saleId, int userId, CancellationToken ct)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Sales.View", ct);
        IQueryable<Sale> query = db.Sales.AsNoTracking().Include(x => x.Items).Where(x => x.Id == saleId);
        if (!Permissions.Has(role, "Sales.ViewAll")) query = query.Where(x => x.UserId == userId);
        var sale = await query.SingleOrDefaultAsync(ct) ?? throw new PosRuleException("Sale was not found.");
        if (sale.Status == "Voided") throw new PosRuleException("A voided sale receipt cannot be reprinted.");
        return sale;
    }

    public async Task<List<Sale>> SearchAsync(int userId, string invoiceFilter, int take = 300, CancellationToken ct = default,
        DateOnly? from = null, DateOnly? through = null, string? status = null)
        => (await SearchPageAsync(userId, invoiceFilter, 0, take, ct, from, through, status)).Items;

    public async Task<SaleSearchPage> SearchPageAsync(int userId, string invoiceFilter, int pageIndex, int pageSize = 100,
        CancellationToken ct = default, DateOnly? from = null, DateOnly? through = null, string? status = null)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Sales.View", ct);
        pageIndex = Math.Max(0, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 500);
        if (from is DateOnly startDate && through is DateOnly endDate && startDate > endDate)
            throw new PosRuleException("The start date must be on or before the end date.");
        IQueryable<Sale> query = db.Sales.AsNoTracking();
        if (!Permissions.Has(role, "Sales.ViewAll")) query = query.Where(x => x.UserId == userId);
        var key = invoiceFilter.Trim(); if (key.Length > 0) query = query.Where(x => x.InvoiceNumber.Contains(key));
        if (from is DateOnly fromDate)
        {
            var localStart = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            query = query.Where(x => x.SoldAtUtc >= TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local));
        }
        if (through is DateOnly throughDate)
        {
            var localEnd = throughDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            query = query.Where(x => x.SoldAtUtc < TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local));
        }
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedStatus = status.Trim();
            if (!new[] { "Completed", "Credit", "Returned", "Voided" }.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
                throw new PosRuleException("Choose a valid sale status filter.");
            query = query.Where(x => x.Status == normalizedStatus);
        }
        var totalCount = await query.CountAsync(ct);
        var offset = (int)Math.Min((long)pageIndex * pageSize, int.MaxValue);
        var items = await query.Include(x => x.Items).OrderByDescending(x => x.SoldAtUtc).ThenByDescending(x => x.Id)
            .Skip(offset).Take(pageSize).ToListAsync(ct);
        return new SaleSearchPage(items, totalCount, pageIndex, pageSize);
    }

    public async Task VoidAsync(int saleId, int userId, string reason, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Void", ct);
        if (string.IsNullOrWhiteSpace(reason)) throw new PosRuleException("Enter a reason for voiding the sale.");
        reason = reason.Trim();
        if (reason.Length > 500) throw new PosRuleException("The void reason must be 500 characters or fewer.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(x => x.Items).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == saleId, ct)
            ?? throw new PosRuleException("Sale was not found.");
        if (sale.Status == "Voided") throw new PosRuleException("This sale has already been voided.");
        if (await db.Returns.AnyAsync(x => x.SaleId == sale.Id, ct))
            throw new PosRuleException("A sale with a processed return cannot be voided. Process the required return instead.");

        var now = DateTime.UtcNow;
        foreach (var item in sale.Items)
        {
            var product = await db.Products.SingleOrDefaultAsync(x => x.Id == item.ProductId, ct)
                ?? throw new PosRuleException($"Product {item.ProductName} no longer exists; sale cannot be safely voided.");
            product.CurrentStock += item.Quantity;
            product.StockVersion++;
            product.UpdatedAtUtc = now;
            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id, QuantityDelta = item.Quantity, BalanceAfter = product.CurrentStock,
                Reason = "SaleVoid", Reference = sale.InvoiceNumber, UserId = userId
            });
        }
        foreach (var payment in sale.Payments.Where(x => x.Amount > 0).ToArray())
            db.Payments.Add(new Payment { SaleId = sale.Id, Amount = -payment.Amount, Method = payment.Method,
                PaidAtUtc = now, Reference = $"VOID-{sale.InvoiceNumber}" });

        sale.Status = "Voided";
        sale.RemainingAmount = 0;
        sale.UpdatedAtUtc = now;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Void", EntityName = "Sale", EntityId = sale.InvoiceNumber,
            Description = $"Voided sale {sale.InvoiceNumber}. Reason: {reason}" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Sale> CompleteAsync(IReadOnlyCollection<(int ProductId, decimal Quantity)> cart, int userId,
        int? customerId, decimal billDiscount, decimal paid, string paymentMethod, string? notes, CancellationToken ct = default, int? heldSaleId = null,
        IReadOnlyCollection<(string Method, decimal Amount)>? paymentBreakdown = null,
        IReadOnlyDictionary<int, decimal>? lineDiscountRates = null)
    {
        if (cart.Count == 0) throw new PosRuleException("The cart is empty.");
        if (cart.Any(x => x.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(cart), "Quantities must be positive.");
        foreach (var line in cart) ServiceInputValidation.RequireDecimalScale(line.Quantity, "Sale quantity");
        ServiceInputValidation.RequireDecimalScale(billDiscount, "Bill discount");
        ServiceInputValidation.RequireDecimalScale(paid, "Payment amount");
        if (lineDiscountRates is not null)
            foreach (var discount in lineDiscountRates.Values) ServiceInputValidation.RequireDecimalScale(discount, "Item discount rate");
        if (paymentBreakdown is not null)
            foreach (var tender in paymentBreakdown) ServiceInputValidation.RequireDecimalScale(tender.Amount, "Tender amount");
        if (string.IsNullOrWhiteSpace(paymentMethod)) throw new PosRuleException("Choose a supported payment method.");
        paymentMethod = paymentMethod.Trim().ToUpperInvariant() switch
        {
            "CASH" => "Cash",
            "CARD" => "Card",
            "CREDIT" => "Credit",
            "MIXED" => "Mixed",
            _ => throw new PosRuleException("Choose Cash, Card, Credit, or Mixed as the payment method.")
        };
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        if (paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            await ServiceAuthorization.RequireAsync(db, userId, "Sales.Credit", ct);
        var grouped = cart.GroupBy(x => x.ProductId).Select(g => (Id: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var products = await db.Products.Where(p => grouped.Select(x => x.Id).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != grouped.Length) throw new PosRuleException("One or more products no longer exist.");
        if (lineDiscountRates is not null && lineDiscountRates.Keys.Any(id => !products.ContainsKey(id)))
            throw new PosRuleException("A discount was supplied for a product that is not in the cart.");
        var effectiveDiscountRates = grouped.ToDictionary(x => x.Id, x => lineDiscountRates?.GetValueOrDefault(x.Id, products[x.Id].DiscountRate) ?? products[x.Id].DiscountRate);
        if (effectiveDiscountRates.Values.Any(x => x is < 0 or > 100)) throw new PosRuleException("Item discounts must be between 0 and 100 percent.");
        if (billDiscount > 0 || effectiveDiscountRates.Any(x => x.Value != products[x.Key].DiscountRate))
            await ServiceAuthorization.RequireAsync(db, userId, "Sales.Discount", ct);
        Customer? selectedCustomer = null;
        if (customerId is not null)
            selectedCustomer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct)
                ?? throw new PosRuleException("Customer is not active or does not exist.");
        foreach (var item in grouped)
        {
            var product = products[item.Id];
            if (!product.IsActive) throw new PosRuleException($"{product.Name} is inactive.");
            if (product.CurrentStock < item.Quantity) throw new PosRuleException($"Insufficient stock for {product.Name}. Available: {product.CurrentStock}.");
        }
        var lines = grouped.Select(x => { var p = products[x.Id]; return new SaleLine(x.Quantity, p.SalePrice, p.PurchasePrice, effectiveDiscountRates[x.Id], p.TaxRate); }).ToArray();
        var totals = SaleCalculator.Calculate(lines, billDiscount, paid);
        if (paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase))
        {
            if (selectedCustomer is null) throw new PosRuleException("Select a customer before completing a credit sale.");
            if (selectedCustomer.CreditLimit <= 0) throw new PosRuleException("Set a positive credit limit for this customer before making a credit sale.");
            var currentBalances = await db.Sales.Where(s => s.CustomerId == selectedCustomer.Id && s.Status != "Voided" && s.RemainingAmount > 0).Select(s => s.RemainingAmount).ToListAsync(ct);
            var accountReceipts = await db.CustomerAccountPayments.Where(x => x.CustomerId == selectedCustomer.Id).Select(x => x.Amount).ToListAsync(ct);
            var currentAccountBalance = Math.Max(0, selectedCustomer.OpeningBalance + currentBalances.Sum() - accountReceipts.Sum());
            if (currentAccountBalance + Math.Max(0, totals.GrandTotal - paid) > selectedCustomer.CreditLimit)
                throw new PosRuleException("This sale exceeds the customer's available credit limit.");
        }
        if (paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase) && customerId is null)
            throw new PosRuleException("Select a customer before completing a credit sale.");
        if (paid > totals.GrandTotal && !paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException("Overpayment is only accepted for cash sales.");
        if (paid < totals.GrandTotal && !paymentMethod.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException("Only credit sales can have an outstanding balance.");
        (string Method, decimal Amount)[]? tenders = null;
        if (paymentMethod.Equals("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            if (paymentBreakdown is null || paymentBreakdown.Count == 0
                || paymentBreakdown.Any(x => x.Amount < 0 || string.IsNullOrWhiteSpace(x.Method)
                    || !new[] { "Cash", "Card" }.Contains(x.Method.Trim(), StringComparer.OrdinalIgnoreCase)))
                throw new PosRuleException("Mixed payment requires non-negative cash/card amounts.");
            tenders = paymentBreakdown.Where(x => x.Amount > 0).Select(x => (
                Method: x.Method.Trim().Equals("Cash", StringComparison.OrdinalIgnoreCase) ? "Cash" : "Card",
                Amount: decimal.Round(x.Amount, 2, MidpointRounding.AwayFromZero))).ToArray();
            if (tenders.Length == 0 || tenders.Any(x => x.Item1 is not ("Cash" or "Card")))
                throw new PosRuleException("Mixed payment supports cash and card tender only.");
            if (tenders.Sum(x => x.Item2) != paid || paid != totals.GrandTotal)
                throw new PosRuleException("Cash and card allocations must add up exactly to the sale total.");
        }
        else if (paymentBreakdown is not null)
            throw new PosRuleException("Tender allocations are only valid for mixed payments.");
        var now = DateTime.UtcNow;
        var prefix = await db.Settings.Where(x => x.Key == "InvoicePrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "INV";
        var invoiceFormat = await db.Settings.Where(x => x.Key == "InvoiceNumberFormat").Select(x => x.Value).SingleOrDefaultAsync(ct);
        var invoice = InvoiceNumberFormatter.Format(invoiceFormat, prefix, now.Year,
            await db.Sales.CountAsync(s => s.SoldAtUtc.Year == now.Year, ct) + 1);
        var sale = new Sale { InvoiceNumber = invoice, UserId = userId, CustomerId = customerId, SoldAtUtc = now, Subtotal = totals.Subtotal,
            Discount = totals.Discount, Tax = totals.Tax, GrandTotal = totals.GrandTotal, PaidAmount = Math.Min(paid, totals.GrandTotal),
            RemainingAmount = Math.Max(0, totals.GrandTotal - paid), CashReceivedAmount = paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase) ? paid : tenders?.Where(x => x.Method == "Cash").Sum(x => x.Amount) ?? 0,
            PaymentMethod = paymentMethod, Status = paid < totals.GrandTotal ? "Credit" : "Completed", Notes = notes };
        var saleLines = grouped.OrderBy(x => x.Quantity * products[x.Id].SalePrice
            * (1 - effectiveDiscountRates[x.Id] / 100m)).ToArray();
        var taxableSubtotal = saleLines.Sum(x => x.Quantity * products[x.Id].SalePrice
            * (1 - effectiveDiscountRates[x.Id] / 100m));
        var lineAmounts = saleLines.Select(item =>
        {
            var product = products[item.Id];
            var gross = item.Quantity * product.SalePrice;
            var taxableBase = gross * (1 - effectiveDiscountRates[item.Id] / 100m);
            var lineDiscount = gross - taxableBase;
            var billDiscountShare = taxableSubtotal == 0 ? 0 : billDiscount * taxableBase / taxableSubtotal;
            var discount = decimal.Round(lineDiscount + billDiscountShare, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round((taxableBase - billDiscountShare) * product.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            var total = decimal.Round(gross - discount + tax, 2, MidpointRounding.AwayFromZero);
            return (Item: item, Product: product, Discount: discount, Tax: tax, Total: total, TaxableBase: taxableBase);
        }).ToArray();
        var discountRemainder = totals.Discount - lineAmounts.Sum(x => x.Discount);
        var discountTarget = Array.FindLastIndex(lineAmounts, x => x.TaxableBase == lineAmounts.Max(y => y.TaxableBase));
        if (discountTarget >= 0) lineAmounts[discountTarget].Discount += discountRemainder;
        var taxRemainder = totals.Tax - lineAmounts.Sum(x => x.Tax);
        var taxTarget = Array.FindLastIndex(lineAmounts, x => x.Tax == lineAmounts.Max(y => y.Tax));
        if (taxTarget >= 0) lineAmounts[taxTarget].Tax += taxRemainder;
        var totalRemainder = totals.GrandTotal - lineAmounts.Sum(x => x.Total);
        var totalTarget = Array.FindLastIndex(lineAmounts, x => x.Total == lineAmounts.Max(y => y.Total));
        if (totalTarget >= 0) lineAmounts[totalTarget].Total += totalRemainder;

        foreach (var line in lineAmounts)
        {
            var item = line.Item; var p = line.Product; p.CurrentStock -= item.Quantity; p.StockVersion++; p.UpdatedAtUtc = now;
            sale.Items.Add(new SaleItem { ProductId = p.Id, ProductName = p.Name, Quantity = item.Quantity, UnitPrice = p.SalePrice,
                UnitCost = p.PurchasePrice, Discount = line.Discount, Tax = line.Tax, Total = line.Total });
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = -item.Quantity, BalanceAfter = p.CurrentStock, Reason = "Sale", Reference = invoice, UserId = userId });
        }
        if (heldSaleId is int heldId)
        {
            var held = await db.HeldSales.SingleOrDefaultAsync(x => x.Id == heldId && x.UserId == userId, ct)
                ?? throw new PosRuleException("The held sale no longer exists or belongs to another user.");
            db.HeldSales.Remove(held);
            db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Complete", EntityName = "HeldSale", EntityId = held.Id.ToString(), Description = $"Completed held sale as {invoice}." });
        }
        if (sale.PaidAmount > 0)
        {
            if (tenders is not null)
                foreach (var tender in tenders) sale.Payments.Add(new Payment { Amount = tender.Amount, Method = tender.Method });
            else sale.Payments.Add(new Payment { Amount = sale.PaidAmount, Method = paymentMethod });
        }
        db.Sales.Add(sale); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Create", EntityName = "Sale", EntityId = invoice, Description = $"Completed sale {invoice}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return sale;
    }
}

public sealed record SaleReceiptData(Sale Sale, IReadOnlyDictionary<string, string> Settings, string CashierName, string? CustomerName);

public sealed record HeldSaleLine(int ProductId, decimal Quantity, decimal? DiscountRate = null);
public sealed record SaleProductLookup(int ProductId, string ProductName, decimal SalePrice, decimal TaxRate, decimal DiscountRate,
    decimal AvailableStock, string Sku = "", string? Barcode = null)
{
    public string DisplayLabel => string.Join(" · ", new[] { ProductName, Sku, Barcode, $"Price {SalePrice:N2}", $"Stock {AvailableStock:N2}" }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}
public sealed record HeldSaleSummary(int Id, DateTime HeldAtUtc, string? Notes, int? CustomerId, decimal BillDiscount, int ItemCount)
{
    public string DisplayLabel => $"{HeldAtUtc.ToLocalTime():HH:mm} · {ItemCount} line(s) · {Notes ?? "Held sale"}{(BillDiscount > 0 ? $" · discount {BillDiscount:N2}" : "")}";
}
public sealed record HeldSaleProduct(int ProductId, string ProductName, decimal Quantity, decimal SalePrice, decimal TaxRate, decimal DiscountRate, decimal AvailableStock);
public sealed record HeldSaleSnapshot(HeldSaleSummary Sale, IReadOnlyList<HeldSaleProduct> Items);

public sealed class HeldSaleService(PosDbContext db)
{
    public async Task<IReadOnlyList<HeldSaleSummary>> SearchAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        var heldSales = await db.HeldSales.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.HeldAtUtc).ToListAsync(ct);
        return heldSales.Select(x => new HeldSaleSummary(x.Id, x.HeldAtUtc, x.Notes, x.CustomerId, x.BillDiscount,
            JsonSerializer.Deserialize<List<HeldSaleLine>>(x.ItemsJson)?.Count ?? 0)).ToArray();
    }

    public async Task<HeldSaleSummary> HoldAsync(IReadOnlyCollection<(int ProductId, decimal Quantity)> lines, int userId, int? customerId = null, string? notes = null, CancellationToken ct = default, int? replaceHeldSaleId = null, decimal billDiscount = 0, IReadOnlyDictionary<int, decimal>? discountRates = null)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0)) throw new ArgumentException("A held sale must contain items with positive quantities.", nameof(lines));
        foreach (var line in lines) ServiceInputValidation.RequireDecimalScale(line.Quantity, "Held-sale quantity");
        ServiceInputValidation.RequireDecimalScale(billDiscount, "Bill discount");
        if (discountRates is not null)
            foreach (var discount in discountRates.Values) ServiceInputValidation.RequireDecimalScale(discount, "Item discount rate");
        if (billDiscount < 0) throw new ArgumentOutOfRangeException(nameof(billDiscount));
        if (discountRates is not null && (discountRates.Any(x => x.Value is < 0 or > 100)
            || discountRates.Keys.Any(id => !lines.Any(line => line.ProductId == id))))
            throw new PosRuleException("Held-sale item discounts must belong to a cart product and be between 0 and 100 percent.");
        var grouped = lines.GroupBy(x => x.ProductId).Select(g => new HeldSaleLine(g.Key, g.Sum(x => x.Quantity), discountRates?.GetValueOrDefault(g.Key))).ToArray();
        var productIds = grouped.Select(x => x.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, x => x.DiscountRate, ct);
        if (products.Count != productIds.Length) throw new PosRuleException("One or more products are inactive or no longer exist.");
        if (billDiscount > 0 || discountRates?.Any(x => x.Value != products.GetValueOrDefault(x.Key, x.Value)) == true)
            await ServiceAuthorization.RequireAsync(db, userId, "Sales.Discount", ct);
        if (customerId is int id && !await db.Customers.AnyAsync(x => x.Id == id && x.IsActive, ct)) throw new PosRuleException("Customer is inactive or no longer exists.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (replaceHeldSaleId is int replacedId)
        {
            var previous = await db.HeldSales.SingleOrDefaultAsync(x => x.Id == replacedId && x.UserId == userId, ct)
                ?? throw new PosRuleException("The held sale no longer exists or belongs to another user.");
            db.HeldSales.Remove(previous);
        }
        var held = new HeldSale { UserId = userId, CustomerId = customerId, HeldAtUtc = DateTime.UtcNow, ItemsJson = JsonSerializer.Serialize(grouped), Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), BillDiscount = billDiscount };
        db.HeldSales.Add(held);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Hold", EntityName = "HeldSale", Description = $"Held a sale with {grouped.Length} product line(s)." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new HeldSaleSummary(held.Id, held.HeldAtUtc, held.Notes, held.CustomerId, held.BillDiscount, grouped.Length);
    }

    public async Task<HeldSaleSnapshot> ResumeAsync(int heldSaleId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        var held = await db.HeldSales.SingleOrDefaultAsync(x => x.Id == heldSaleId && x.UserId == userId, ct)
            ?? throw new PosRuleException("The held sale no longer exists or belongs to another user.");
        var items = JsonSerializer.Deserialize<List<HeldSaleLine>>(held.ItemsJson)
            ?? throw new PosRuleException("The held sale contains invalid item data.");
        if (held.CustomerId is int customerId && !await db.Customers.AnyAsync(x => x.Id == customerId && x.IsActive, ct))
            throw new PosRuleException("The held sale customer is inactive or no longer exists.");
        var productIds = items.Select(x => x.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (products.Count != productIds.Length) throw new PosRuleException("A held sale product is inactive or no longer exists.");
        var lines = items.Select(x =>
        {
            var product = products[x.ProductId];
            return new HeldSaleProduct(product.Id, product.Name, x.Quantity, product.SalePrice, product.TaxRate, x.DiscountRate ?? product.DiscountRate, product.CurrentStock);
        }).ToArray();
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Resume", EntityName = "HeldSale", EntityId = held.Id.ToString(), Description = "Resumed a held sale." });
        await db.SaveChangesAsync(ct);
        return new HeldSaleSnapshot(new HeldSaleSummary(held.Id, held.HeldAtUtc, held.Notes, held.CustomerId, held.BillDiscount, lines.Length), lines);
    }

    public async Task CancelAsync(int? heldSaleId, int userId, int itemCount, decimal total, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        if (itemCount < 0 || total < 0) throw new ArgumentOutOfRangeException(nameof(itemCount));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entityName = "SaleDraft";
        string? entityId = null;
        if (heldSaleId is int id)
        {
            var heldSale = await db.HeldSales.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
                ?? throw new PosRuleException("The held sale no longer exists or belongs to another user.");
            db.HeldSales.Remove(heldSale);
            entityName = "HeldSale";
            entityId = id.ToString(CultureInfo.InvariantCulture);
        }
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Cancel", EntityName = entityName, EntityId = entityId,
            Description = $"Cancelled a sale with {itemCount} product line(s), estimated total {total:N2}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

public sealed record PurchaseLineInput(int ProductId, decimal Quantity, decimal UnitCost, decimal DiscountRate = 0, decimal TaxRate = 0);

public sealed class PurchaseService(PosDbContext db)
{
    public Task<Purchase> ReceiveAsync(int supplierId, int userId,
        IReadOnlyCollection<(int ProductId, decimal Quantity, decimal UnitCost)> lines, CancellationToken ct = default) =>
        ReceiveAsync(supplierId, userId, lines.Select(x => new PurchaseLineInput(x.ProductId, x.Quantity, x.UnitCost)).ToArray(), ct);

    public Task<Purchase> ReceiveAsync(int supplierId, int userId, IReadOnlyCollection<PurchaseLineInput> lines, CancellationToken ct = default) =>
        ReceiveCoreAsync(supplierId, userId, lines, 0, "Cash", null, ct);

    public Task<Purchase> ReceiveWithPaymentAsync(int supplierId, int userId, IReadOnlyCollection<PurchaseLineInput> lines,
        decimal amountPaid, string paymentMethod, string? paymentReference = null, CancellationToken ct = default) =>
        ReceiveCoreAsync(supplierId, userId, lines, amountPaid, paymentMethod, paymentReference, ct);

    private async Task<Purchase> ReceiveCoreAsync(int supplierId, int userId, IReadOnlyCollection<PurchaseLineInput> lines,
        decimal amountPaid, string paymentMethod, string? paymentReference, CancellationToken ct)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Manage", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0 || x.UnitCost < 0
            || x.DiscountRate is < 0 or > 100 || x.TaxRate is < 0 or > 100))
            throw new ArgumentException("Purchase requires positive quantities, non-negative costs, and discount/tax rates between 0 and 100 percent.", nameof(lines));
        foreach (var line in lines)
        {
            ServiceInputValidation.RequireDecimalScale(line.Quantity, "Purchase quantity");
            ServiceInputValidation.RequireDecimalScale(line.DiscountRate, "Purchase discount rate");
            ServiceInputValidation.RequireDecimalScale(line.TaxRate, "Purchase tax rate");
        }
        ServiceInputValidation.RequireDecimalScale(amountPaid, "Initial payment amount");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var grouped = lines.GroupBy(x => (x.ProductId, x.UnitCost, x.DiscountRate, x.TaxRate))
            .Select(g => (g.Key.ProductId, Quantity: g.Sum(x => x.Quantity), g.Key.UnitCost, g.Key.DiscountRate, g.Key.TaxRate)).ToArray();
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct)) throw new PosRuleException("Supplier is not active or does not exist.");
        var products = await db.Products.Where(p => grouped.Select(x => x.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != grouped.Select(x => x.ProductId).Distinct().Count()) throw new PosRuleException("A purchase product was not found.");
        if (products.Values.Any(product => !product.IsActive))
            throw new PosRuleException("Inactive products cannot be received on a purchase.");
        var expectedTotal = grouped.Sum(item =>
        {
            var gross = item.Quantity * item.UnitCost;
            var discount = decimal.Round(gross * item.DiscountRate / 100m, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round((gross - discount) * item.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            return decimal.Round(gross - discount + tax, 2, MidpointRounding.AwayFromZero);
        });
        if (amountPaid < 0) throw new PosRuleException("Initial payment cannot be negative.");
        if (amountPaid > expectedTotal) throw new PosRuleException($"Initial payment cannot exceed the invoice total of {expectedTotal:N2}.");
        if (amountPaid > 0)
        {
            await ServiceAuthorization.RequireAsync(db, userId, "Payments.Make", ct);
            if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(paymentMethod, StringComparer.OrdinalIgnoreCase))
                throw new PosRuleException("Choose Cash, Card, or Bank transfer for the initial payment.");
        }
        foreach (var received in grouped.GroupBy(x => x.ProductId))
        {
            var product = products[received.Key];
            var newStock = product.CurrentStock + received.Sum(x => x.Quantity);
            if (product.MaximumStock > 0 && newStock > product.MaximumStock)
                throw new PosRuleException($"Receiving this purchase would exceed the maximum stock of {product.Name} ({product.MaximumStock}).");
        }
        var now = DateTime.UtcNow; var prefix = await db.Settings.Where(x => x.Key == "PurchasePrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "PUR";
        var invoiceFormat = await db.Settings.Where(x => x.Key == "InvoiceNumberFormat").Select(x => x.Value).SingleOrDefaultAsync(ct);
        var invoice = InvoiceNumberFormatter.Format(invoiceFormat, prefix, now.Year,
            await db.Purchases.CountAsync(p => p.PurchasedAtUtc.Year == now.Year, ct) + 1);
        var purchase = new Purchase { InvoiceNumber = invoice, SupplierId = supplierId, UserId = userId, PurchasedAtUtc = now, PaymentStatus = "Unpaid" };
        foreach (var item in grouped)
        {
            var p = products[item.ProductId];
            var gross = item.Quantity * item.UnitCost;
            var discount = decimal.Round(gross * item.DiscountRate / 100m, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round((gross - discount) * item.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            var total = decimal.Round(gross - discount + tax, 2, MidpointRounding.AwayFromZero);
            p.CurrentStock += item.Quantity;
            p.StockVersion++;
            p.PurchasePrice = decimal.Round(item.UnitCost * (1 - item.DiscountRate / 100m), 4, MidpointRounding.AwayFromZero);
            p.UpdatedAtUtc = now;
            purchase.Items.Add(new PurchaseItem { ProductId = p.Id, Quantity = item.Quantity, UnitCost = item.UnitCost,
                Discount = discount, Tax = tax, Total = total });
            purchase.Discount += discount;
            purchase.Tax += tax;
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = item.Quantity, BalanceAfter = p.CurrentStock, Reason = "Purchase", Reference = invoice, UserId = userId });
        }
        purchase.Total = purchase.Items.Sum(x => x.Total);
        if (amountPaid > purchase.Total) throw new PosRuleException($"Initial payment cannot exceed the invoice total of {purchase.Total:N2}.");
        purchase.PaidAmount = amountPaid;
        purchase.RemainingAmount = purchase.Total - amountPaid;
        purchase.PaymentStatus = purchase.RemainingAmount == 0 ? "Paid" : amountPaid > 0 ? "Partial" : "Unpaid";
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(ct); // assign the invoice ID before linking its initial supplier payment
        if (amountPaid > 0)
        {
            db.SupplierPayments.Add(new SupplierPayment { SupplierId = supplierId, PurchaseId = purchase.Id, UserId = userId,
                Amount = amountPaid, Method = paymentMethod.Trim(), Reference = string.IsNullOrWhiteSpace(paymentReference) ? null : paymentReference.Trim(), PaidAtUtc = now });
            db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Purchase", EntityId = invoice,
                Description = $"Paid {amountPaid:N2} toward purchase invoice; {purchase.RemainingAmount:N2} remains." });
        }
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
        foreach (var line in lines) ServiceInputValidation.RequireDecimalScale(line.Quantity, "Sale return quantity");
        if (string.IsNullOrWhiteSpace(reason)) throw new PosRuleException("Enter a reason for the sale return.");
        reason = reason.Trim();
        if (reason.Length > 500) throw new PosRuleException("The sale return reason must be 500 characters or fewer.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId, ct);
        if (sale.Status == "Voided") throw new PosRuleException("A voided sale cannot be returned.");
        var saleItemIds = sale.Items.Select(i => i.Id).ToArray();
        var previousReturns = await db.ReturnItems.AsNoTracking().Where(r => saleItemIds.Contains(r.SaleItemId))
            .GroupBy(r => r.SaleItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity), Refund = g.Sum(x => x.Refund) }).ToListAsync(ct);
        var already = previousReturns.ToDictionary(x => x.Id, x => x.Quantity);
        var alreadyRefunded = previousReturns.ToDictionary(x => x.Id, x => x.Refund);
        var now = DateTime.UtcNow; var number = $"RET-{now:yyyy}-{await db.Returns.CountAsync(r => r.ReturnedAtUtc.Year == now.Year, ct) + 1:000000}";
        var ret = new SaleReturn { ReturnNumber = number, SaleId = saleId, UserId = userId, ReturnedAtUtc = now, Reason = reason };
        var requested = lines.GroupBy(x => x.SaleItemId).Select(g => (SaleItemId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        foreach (var request in requested)
        {
            var item = sale.Items.SingleOrDefault(i => i.Id == request.SaleItemId) ?? throw new PosRuleException("Return item is not part of this sale.");
            if (request.Quantity + already.GetValueOrDefault(item.Id) > item.Quantity) throw new PosRuleException($"Return quantity exceeds quantity sold for {item.ProductName}.");
            var product = await db.Products.SingleAsync(p => p.Id == item.ProductId, ct);
            product.CurrentStock += request.Quantity;
            product.StockVersion++;
            product.UpdatedAtUtc = now;
            var billDiscount = Math.Max(0, sale.Discount - sale.Items.Sum(i => i.Discount)); // Legacy invoices stored bill discount only at sale level.
            var lineShare = sale.Items.Sum(i => i.Total) == 0 ? 0 : billDiscount * item.Total / sale.Items.Sum(i => i.Total);
            var refundableLineTotal = item.Total - lineShare;
            var completesReturn = request.Quantity + already.GetValueOrDefault(item.Id) >= item.Quantity;
            var refund = completesReturn
                ? refundableLineTotal - alreadyRefunded.GetValueOrDefault(item.Id)
                : decimal.Round(refundableLineTotal * request.Quantity / item.Quantity, 2, MidpointRounding.AwayFromZero);
            if (refund < 0) throw new PosRuleException($"Previous refunds exceed the refundable value for {item.ProductName}.");
            ret.Items.Add(new SaleReturnItem { SaleItemId = item.Id, ProductId = item.ProductId, Quantity = request.Quantity, Refund = refund }); ret.Total += refund;
            db.StockMovements.Add(new StockMovement { ProductId = item.ProductId, QuantityDelta = request.Quantity, BalanceAfter = product.CurrentStock, Reason = "SaleReturn", Reference = number, UserId = userId });
        }
        var outstandingBeforeReturn = sale.RemainingAmount;
        var cashRefund = Math.Max(0, ret.Total - outstandingBeforeReturn);
        if (cashRefund > sale.PaidAmount) throw new PosRuleException("Refund exceeds the net amount paid on this sale.");
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

public sealed record ReturnablePurchaseItem(int PurchaseItemId, int ProductId, string ProductName, decimal QuantityRemaining, decimal ValueRemaining)
{
    public decimal UnitReturnValue => QuantityRemaining == 0 ? 0 : ValueRemaining / QuantityRemaining;
}

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
            ?? throw new PosRuleException("Purchase was not found.");
        var itemIds = purchase.Items.Select(x => x.Id).ToArray();
        var returned = await db.PurchaseReturnItems.AsNoTracking().Where(x => itemIds.Contains(x.PurchaseItemId))
            .GroupBy(x => x.PurchaseItemId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity), Total = g.Sum(x => x.Total) })
            .ToDictionaryAsync(x => x.Id, x => (x.Quantity, x.Total), ct);
        var productIds = purchase.Items.Select(x => x.ProductId).Distinct().ToArray();
        var names = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return purchase.Items.Select(item => new ReturnablePurchaseItem(item.Id, item.ProductId,
            names.GetValueOrDefault(item.ProductId, "Unknown product"), item.Quantity - returned.GetValueOrDefault(item.Id).Quantity,
            item.Total - returned.GetValueOrDefault(item.Id).Total))
            .Where(x => x.QuantityRemaining > 0).ToArray();
    }

    public async Task<PurchaseReturn> ReturnAsync(int purchaseId, int userId, IReadOnlyCollection<(int PurchaseItemId, decimal Quantity)> lines, string reason, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Purchases.Return", ct);
        if (lines.Count == 0 || lines.Any(x => x.Quantity <= 0)) throw new ArgumentException("Return requires positive item quantities.", nameof(lines));
        foreach (var line in lines) ServiceInputValidation.RequireDecimalScale(line.Quantity, "Purchase return quantity");
        if (string.IsNullOrWhiteSpace(reason)) throw new PosRuleException("Enter a reason for the purchase return.");
        reason = reason.Trim();
        if (reason.Length > 500) throw new PosRuleException("The purchase return reason must be 500 characters or fewer.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var purchase = await db.Purchases.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == purchaseId, ct)
            ?? throw new PosRuleException("Purchase was not found.");
        var requested = lines.GroupBy(x => x.PurchaseItemId).Select(g => (PurchaseItemId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToArray();
        if (requested.Any(x => purchase.Items.All(i => i.Id != x.PurchaseItemId))) throw new PosRuleException("Return item is not part of this purchase.");
        var itemIds = purchase.Items.Select(x => x.Id).ToArray();
        var previousReturns = await db.PurchaseReturnItems.Where(x => itemIds.Contains(x.PurchaseItemId)).GroupBy(x => x.PurchaseItemId)
            .Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity), Total = g.Sum(x => x.Total) }).ToListAsync(ct);
        var already = previousReturns.ToDictionary(x => x.Id, x => (x.Quantity, x.Total));
        var now = DateTime.UtcNow;
        var prefix = await db.Settings.Where(x => x.Key == "PurchaseReturnPrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "PRT";
        var number = $"{prefix}-{now:yyyy}-{await db.PurchaseReturns.CountAsync(x => x.ReturnedAtUtc.Year == now.Year, ct) + 1:000000}";
        var result = new PurchaseReturn { ReturnNumber = number, PurchaseId = purchaseId, UserId = userId, ReturnedAtUtc = now, Reason = reason.Trim() };
        foreach (var request in requested)
        {
            var item = purchase.Items.Single(x => x.Id == request.PurchaseItemId);
            var previouslyReturned = already.GetValueOrDefault(item.Id);
            if (request.Quantity + previouslyReturned.Quantity > item.Quantity)
                throw new PosRuleException("Return quantity exceeds the unreturned quantity received.");
            var product = await db.Products.SingleAsync(x => x.Id == item.ProductId, ct);
            if (request.Quantity > product.CurrentStock)
                throw new PosRuleException($"Cannot return {request.Quantity} units of {product.Name}; only {product.CurrentStock} are in stock.");
            product.CurrentStock -= request.Quantity;
            product.StockVersion++;
            product.UpdatedAtUtc = now;
            var remainingQuantity = item.Quantity - previouslyReturned.Quantity;
            var remainingValue = item.Total - previouslyReturned.Total;
            var total = request.Quantity >= remainingQuantity
                ? remainingValue
                : decimal.Round(remainingValue * request.Quantity / remainingQuantity, 2, MidpointRounding.AwayFromZero);
            result.Items.Add(new PurchaseReturnItem { PurchaseItemId = item.Id, ProductId = product.Id, Quantity = request.Quantity, Total = total });
            result.Total += total;
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, QuantityDelta = -request.Quantity, BalanceAfter = product.CurrentStock, Reason = "PurchaseReturn", Reference = number, UserId = userId });
        }
        var allReturned = await db.PurchaseReturnItems.Where(x => itemIds.Contains(x.PurchaseItemId)).Select(x => new { x.PurchaseItemId, x.Quantity }).ToListAsync(ct);
        var returnedQuantities = allReturned.GroupBy(x => x.PurchaseItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var item in result.Items) returnedQuantities[item.PurchaseItemId] = returnedQuantities.GetValueOrDefault(item.PurchaseItemId) + item.Quantity;
        purchase.Status = purchase.Items.All(item => returnedQuantities.GetValueOrDefault(item.Id) >= item.Quantity) ? "Returned" : "PartiallyReturned";
        purchase.RemainingAmount -= result.Total;
        purchase.PaymentStatus = purchase.RemainingAmount < 0 ? "Credit" : purchase.RemainingAmount == 0 ? "Paid" : purchase.PaidAmount == 0 ? "Unpaid" : "Partial";
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
        ServiceInputValidation.RequireDecimalScale(delta, "Stock adjustment");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.Products.SingleAsync(x => x.Id == productId, ct);
        if (p.CurrentStock + delta < 0) throw new PosRuleException("Stock cannot become negative.");
        if (delta > 0 && p.MaximumStock > 0 && p.CurrentStock + delta > p.MaximumStock)
            throw new PosRuleException($"This adjustment would exceed the maximum stock of {p.Name} ({p.MaximumStock}).");
        p.CurrentStock += delta; p.StockVersion++; p.UpdatedAtUtc = DateTime.UtcNow;
        db.StockMovements.Add(new StockMovement { ProductId = p.Id, QuantityDelta = delta, BalanceAfter = p.CurrentStock, Reason = reason, UserId = userId });
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Adjust", EntityName = "Product", EntityId = p.Id.ToString(), Description = $"Stock adjustment {delta}: {reason}" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}

public sealed record DashboardSnapshot(decimal? TodaySales, decimal? TodayPurchases, decimal? TodayProfit, decimal? TodayExpenses,
    int ProductCount, int LowStockCount, int CustomerCount, int SupplierCount);
public sealed record DashboardTrendPoint(DateOnly Day, decimal Sales, decimal Purchases, decimal? Profit);
public sealed record DashboardTopProduct(int ProductId, string ProductName, decimal UnitsSold, decimal Revenue);
public sealed record DashboardRecentSale(string InvoiceNumber, DateTime SoldAtUtc, decimal Total, string Status);
public sealed record DashboardRecentPurchase(string InvoiceNumber, string SupplierName, DateTime PurchasedAtUtc, decimal Total, string Status);

public sealed class DashboardService(PosDbContext db)
{
    private sealed record ReturnProfitLine(decimal Refund, decimal Quantity, decimal SoldQuantity, decimal Tax, decimal UnitCost);
    private sealed record DashboardDateAmount(DateTime DateUtc, decimal Amount);
    private sealed record DashboardTodayReturn(int Id, decimal Total);
    public async Task<IReadOnlyList<DashboardRecentSale>> GetRecentSalesAsync(int userId, int take = 5, CancellationToken ct = default)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Sales.View", ct);
        take = Math.Clamp(take, 1, 20);
        var sales = db.Sales.AsNoTracking().AsQueryable();
        if (!Permissions.Has(role, "Sales.ViewAll")) sales = sales.Where(x => x.UserId == userId);
        return await sales.OrderByDescending(x => x.SoldAtUtc).ThenByDescending(x => x.Id).Take(take)
            .Select(x => new DashboardRecentSale(x.InvoiceNumber, x.SoldAtUtc, x.GrandTotal, x.Status)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DashboardRecentPurchase>> GetRecentPurchasesAsync(int userId, int take = 5, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAnyAsync(db, userId, ct, "Purchases.Manage", "Purchases.Return");
        take = Math.Clamp(take, 1, 20);
        return await (from purchase in db.Purchases.AsNoTracking()
                      join supplier in db.Suppliers.AsNoTracking() on purchase.SupplierId equals supplier.Id
                      orderby purchase.PurchasedAtUtc descending, purchase.Id descending
                      select new DashboardRecentPurchase(purchase.InvoiceNumber, supplier.Name, purchase.PurchasedAtUtc, purchase.Total, purchase.Status))
            .Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DashboardTopProduct>> GetTopProductsTodayAsync(int userId, int take = 5, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new UnauthorizedAccessException("An active user session is required.");
        var canViewSales = Permissions.Has(user.Role, "Sales.View");
        if (!canViewSales && !Permissions.Has(user.Role, "Products.Manage"))
            throw new UnauthorizedAccessException("Permission required: Sales.View or Products.Manage.");
        take = Math.Clamp(take, 1, 20);
        var day = DateOnly.FromDateTime(DateTime.Today);
        var localStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var start = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);
        var end = TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local);
        var sales = db.Sales.AsNoTracking().Where(s => s.SoldAtUtc >= start && s.SoldAtUtc < end && s.Status != "Voided");
        if (canViewSales && !Permissions.Has(user.Role, "Sales.ViewAll")) sales = sales.Where(s => s.UserId == userId);
        var rows = await (from item in db.SaleItems.AsNoTracking()
                          join sale in sales on item.SaleId equals sale.Id
                          select new { item.Id, item.ProductId, item.ProductName, item.Quantity, item.Total }).ToListAsync(ct);
        var returns = await (from returned in db.ReturnItems.AsNoTracking()
                             join ret in db.Returns.AsNoTracking() on returned.SaleReturnId equals ret.Id
                             where ret.ReturnedAtUtc >= start && ret.ReturnedAtUtc < end
                             select new { returned.SaleItemId, returned.Quantity, returned.Refund }).ToListAsync(ct);
        return rows.GroupBy(x => new { x.ProductId, x.ProductName }).Select(group =>
        {
            var saleItemIds = group.Select(x => x.Id).ToHashSet();
            var returned = returns.Where(x => saleItemIds.Contains(x.SaleItemId)).ToArray();
            return new DashboardTopProduct(group.Key.ProductId, group.Key.ProductName,
                group.Sum(x => x.Quantity) - returned.Sum(x => x.Quantity), group.Sum(x => x.Total) - returned.Sum(x => x.Refund));
        }).Where(x => x.UnitsSold > 0).OrderByDescending(x => x.UnitsSold).ThenBy(x => x.ProductName).Take(take).ToArray();
    }

    public async Task<IReadOnlyList<DashboardTrendPoint>> GetLastSevenDaysAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new UnauthorizedAccessException("An active user session is required.");
        var canViewSales = Permissions.Has(user.Role, "Sales.View");
        var canViewPurchases = Permissions.Has(user.Role, "Purchases.Manage") || Permissions.Has(user.Role, "Purchases.Return");
        var through = DateOnly.FromDateTime(DateTime.Today);
        var from = through.AddDays(-6);
        var start = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local);
        var end = TimeZoneInfo.ConvertTimeToUtc(through.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local);
        var salesQuery = db.Sales.AsNoTracking().Where(x => x.SoldAtUtc >= start && x.SoldAtUtc < end && x.Status != "Voided");
        if (canViewSales && !Permissions.Has(user.Role, "Sales.ViewAll")) salesQuery = salesQuery.Where(x => x.UserId == userId);
        if (!canViewSales) salesQuery = salesQuery.Where(_ => false);
        var sales = await salesQuery.Select(x => new DashboardDateAmount(x.SoldAtUtc, x.GrandTotal)).ToListAsync(ct);
        var saleReturns = canViewSales
            ? await (from returned in db.Returns.AsNoTracking()
                     join sale in db.Sales.AsNoTracking() on returned.SaleId equals sale.Id
                     where returned.ReturnedAtUtc >= start && returned.ReturnedAtUtc < end
                         && (Permissions.Has(user.Role, "Sales.ViewAll") || sale.UserId == userId)
                     select new DashboardDateAmount(returned.ReturnedAtUtc, returned.Total)).ToListAsync(ct)
            : [];
        var purchases = canViewPurchases
            ? await db.Purchases.AsNoTracking().Where(x => x.PurchasedAtUtc >= start && x.PurchasedAtUtc < end && x.Status != "Voided")
                .Select(x => new DashboardDateAmount(x.PurchasedAtUtc, x.Total)).ToListAsync(ct)
            : [];
        var purchaseReturns = canViewPurchases
            ? await db.PurchaseReturns.AsNoTracking().Where(x => x.ReturnedAtUtc >= start && x.ReturnedAtUtc < end)
                .Select(x => new DashboardDateAmount(x.ReturnedAtUtc, x.Total)).ToListAsync(ct)
            : [];
        static DateOnly LocalDate(DateTime date) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(date, DateTimeKind.Utc), TimeZoneInfo.Local));
        var salesByDay = sales.GroupBy(x => LocalDate(x.DateUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var salesReturnsByDay = saleReturns.GroupBy(x => LocalDate(x.DateUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var purchasesByDay = purchases.GroupBy(x => LocalDate(x.DateUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var purchaseReturnsByDay = purchaseReturns.GroupBy(x => LocalDate(x.DateUtc)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var profitByDay = Permissions.Has(user.Role, "Reports.Profit")
            ? (await new ReportsService(db).GetDailyProfitAsync(userId, from, through, ct)).ToDictionary(x => x.Day, x => x.Profit)
            : null;
        return Enumerable.Range(0, 7).Select(offset =>
        {
            var day = from.AddDays(offset);
            return new DashboardTrendPoint(day, salesByDay.GetValueOrDefault(day) - salesReturnsByDay.GetValueOrDefault(day),
                purchasesByDay.GetValueOrDefault(day) - purchaseReturnsByDay.GetValueOrDefault(day), profitByDay?.GetValueOrDefault(day));
        }).ToArray();
    }

    public async Task<DashboardSnapshot> GetTodayAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new UnauthorizedAccessException("An active user session is required.");
        var canViewSales = Permissions.Has(user.Role, "Sales.View");
        var canViewAllSales = Permissions.Has(user.Role, "Sales.ViewAll");
        var canViewPurchases = Permissions.Has(user.Role, "Purchases.Manage") || Permissions.Has(user.Role, "Purchases.Return");
        var canViewExpenses = Permissions.Has(user.Role, "Expenses.Manage") || Permissions.Has(user.Role, "Reports.View");
        var canViewProfit = Permissions.Has(user.Role, "Reports.Profit");
        var lowStockThreshold = await InventorySettings.GetLowStockThresholdAsync(db, ct);
        var localStart = DateTime.Today;
        var start = TimeZoneInfo.ConvertTimeToUtc(localStart, TimeZoneInfo.Local);
        var end = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), TimeZoneInfo.Local);
        var sales = db.Sales.Where(x => x.SoldAtUtc >= start && x.SoldAtUtc < end && x.Status != "Voided");
        if (canViewSales && !canViewAllSales) sales = sales.Where(x => x.UserId == userId);
        if (!canViewSales) sales = sales.Where(_ => false);
        var purchases = db.Purchases.Where(x => x.PurchasedAtUtc >= start && x.PurchasedAtUtc < end && x.Status != "Voided");
        if (!canViewPurchases) purchases = purchases.Where(_ => false);
        var expenses = db.Expenses.Where(x => x.SpentAtUtc >= start && x.SpentAtUtc < end);
        if (!canViewExpenses) expenses = expenses.Where(_ => false);
        var saleRows = await sales.Select(x => new { x.Id, x.GrandTotal, x.Discount }).ToListAsync(ct);
        var returnedToday = canViewSales
            ? await (from returned in db.Returns.AsNoTracking()
                     join sale in db.Sales.AsNoTracking() on returned.SaleId equals sale.Id
                     where returned.ReturnedAtUtc >= start && returned.ReturnedAtUtc < end
                         && (canViewAllSales || sale.UserId == userId)
                     select new DashboardTodayReturn(returned.Id, returned.Total)).ToListAsync(ct)
            : [];
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
        return new DashboardSnapshot(canViewSales ? saleRows.Sum(x => x.GrandTotal) - returnedToday.Sum(x => x.Total) : null,
            canViewPurchases ? purchaseTotals.Sum() : null,
            saleItems is null ? null : saleItems.Sum(x => x.Total - x.Tax - x.UnitCost * x.Quantity)
                - saleRows.Sum(s => s.Discount - saleItems.Where(i => i.SaleId == s.Id).Sum(i => i.Discount)) - returnProfit,
            canViewExpenses ? expenseTotals.Sum() : null,
            await db.Products.CountAsync(x => x.IsActive, ct), await db.Products.CountAsync(x => x.IsActive
                && x.CurrentStock <= (x.MinimumStock > 0 ? x.MinimumStock : lowStockThreshold), ct),
            await db.Customers.CountAsync(x => x.IsActive, ct), await db.Suppliers.CountAsync(x => x.IsActive, ct));
    }
}

public sealed class ProductService(PosDbContext db)
{
    public async Task RecordBarcodeLabelsPrintedAsync(int productId, int copies, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        if (copies is < 1 or > 500) throw new PosRuleException("Print between 1 and 500 labels at a time.");
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, ct)
            ?? throw new PosRuleException("The selected product no longer exists.");
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Print", EntityName = "Product", EntityId = product.Id.ToString(),
            Description = $"Printed {copies} barcode label(s) for {product.Sku}." });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SaleProductLookup>> SearchForSaleAsync(string query, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Create", ct);
        var key = query.Trim();
        if (key.Length == 0) return [];
        return await db.Products.AsNoTracking()
            .Where(p => p.IsActive && (p.Barcode == key || p.Sku == key || p.Name.Contains(key)))
            .OrderBy(p => p.Name).Take(2)
            .Select(p => new SaleProductLookup(p.Id, p.Name, p.SalePrice, p.TaxRate, p.DiscountRate, p.CurrentStock, p.Sku, p.Barcode))
            .ToListAsync(ct);
    }

    public async Task<Product> SaveAsync(Product product, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        var isNew = product.Id == 0;
        if (isNew && product.CurrentStock > 0)
            await ServiceAuthorization.RequireAsync(db, userId, "Inventory.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(product.Name); ArgumentException.ThrowIfNullOrWhiteSpace(product.Sku);
        product.Name = product.Name.Trim();
        if (product.Name.Length > 200) throw new PosRuleException("Product name must be 200 characters or fewer.");
        product.Sku = product.Sku.Trim();
        product.Barcode = ServiceInputValidation.NormalizeOptional(product.Barcode);
        product.Unit = string.IsNullOrWhiteSpace(product.Unit) ? "each" : product.Unit.Trim();
        ServiceInputValidation.RequireMaxLength(product.Sku, "Product SKU", 64);
        ServiceInputValidation.RequireMaxLength(product.Barcode, "Product barcode", 128);
        ServiceInputValidation.RequireMaxLength(product.Unit, "Product unit", 32);
        ServiceInputValidation.RequireMaxLength(product.ImagePath, "Product image path", 1024);
        ServiceInputValidation.RequireMaxLength(product.Description, "Product description", 2000);
        if (product.PurchasePrice < 0 || product.SalePrice < 0 || product.WholesalePrice < 0 || product.MinimumStock < 0 || product.MaximumStock < 0 || product.CurrentStock < 0) throw new ArgumentOutOfRangeException(nameof(product));
        ServiceInputValidation.RequireDecimalScale(product.PurchasePrice, "Purchase price");
        ServiceInputValidation.RequireDecimalScale(product.SalePrice, "Sale price");
        ServiceInputValidation.RequireDecimalScale(product.WholesalePrice, "Wholesale price");
        ServiceInputValidation.RequireDecimalScale(product.MinimumStock, "Minimum stock");
        ServiceInputValidation.RequireDecimalScale(product.MaximumStock, "Maximum stock");
        ServiceInputValidation.RequireDecimalScale(product.CurrentStock, "Current stock");
        ServiceInputValidation.RequireDecimalScale(product.TaxRate, "Tax rate");
        ServiceInputValidation.RequireDecimalScale(product.DiscountRate, "Discount rate");
        if (product.MaximumStock > 0 && product.MinimumStock > product.MaximumStock)
            throw new PosRuleException("Minimum stock cannot exceed the configured maximum stock.");
        if (isNew && product.MaximumStock > 0 && product.CurrentStock > product.MaximumStock)
            throw new PosRuleException("Current stock cannot exceed the configured maximum stock.");
        if (product.TaxRate is < 0 or > 100 || product.DiscountRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(product));
        var duplicate = await db.Products.AnyAsync(p => p.Id != product.Id && (p.Sku == product.Sku || (product.Barcode != null && p.Barcode == product.Barcode)), ct);
        if (duplicate) throw new PosRuleException("SKU or barcode is already assigned to another product.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (isNew) { product.StockVersion = 0; db.Products.Add(product); }
        else
        {
            var existing = await db.Products.SingleAsync(x => x.Id == product.Id, ct);
            if (product.MaximumStock > 0 && existing.CurrentStock > product.MaximumStock)
                throw new PosRuleException("Maximum stock cannot be set below current stock. Reduce stock with an inventory adjustment first.");
            var currentStock = existing.CurrentStock;
            var stockVersion = existing.StockVersion;
            db.Entry(existing).CurrentValues.SetValues(product);
            existing.CurrentStock = currentStock;
            existing.StockVersion = stockVersion;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            product = existing;
        }
        await db.SaveChangesAsync(ct);
        if (isNew && product.CurrentStock > 0)
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, QuantityDelta = product.CurrentStock,
                BalanceAfter = product.CurrentStock, Reason = "OpeningStock", Reference = "Opening stock", UserId = userId });
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
    public async Task<List<Product>> SearchAsync(string query, int userId, int skip = 0, int take = 50,
        string? categoryFilter = null, bool lowStockOnly = false, string sortBy = "Name", CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.View", ct);
        take = Math.Clamp(take, 1, 200);
        var products = db.Products.AsNoTracking().Where(p => p.IsActive
            && (query == "" || p.Name.Contains(query) || p.Sku.Contains(query) || p.Barcode == query));
        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            var category = categoryFilter.Trim();
            products = products.Where(p => p.Category != null && p.Category.Name.Contains(category));
        }
        if (lowStockOnly)
        {
            var lowStockThreshold = await InventorySettings.GetLowStockThresholdAsync(db, ct);
            products = products.Where(p => p.CurrentStock <= (p.MinimumStock > 0 ? p.MinimumStock : lowStockThreshold));
        }
        products = sortBy.Trim().ToLowerInvariant() switch
        {
            "sku" => products.OrderBy(p => p.Sku).ThenBy(p => p.Id),
            "sale price" => products.OrderBy(p => p.SalePrice).ThenBy(p => p.Id),
            "stock" => products.OrderBy(p => p.CurrentStock).ThenBy(p => p.Id),
            _ => products.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };
        return await products.Skip(Math.Max(0, skip)).Take(take).ToListAsync(ct);
    }
}
