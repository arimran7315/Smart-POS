using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

public sealed record QuotationLineInput(int ProductId, decimal Quantity, decimal? DiscountRate);
public sealed record QuotationSummary(int Id, string QuoteNumber, int? CustomerId, string CustomerName, DateTime QuotedAtUtc,
    DateTime? ValidUntilUtc, decimal GrandTotal, string Status, int? ConvertedSaleId)
{
    public string DisplayLabel => $"{QuoteNumber} · {CustomerName} · {GrandTotal:N2} · {Status}";
}
public sealed record QuotationItemView(int ProductId, string ProductName, decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal Discount, decimal Tax, decimal Total);

public sealed class QuotationService(PosDbContext db)
{
    public async Task<Quotation> CreateAsync(int? customerId, IReadOnlyCollection<QuotationLineInput> lines, decimal billDiscount,
        string? notes, DateOnly? validUntil, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Quote", ct);
        if (lines.Count == 0) throw new PosRuleException("Add at least one item to the quotation.");
        if (lines.Any(x => x.Quantity <= 0)) throw new PosRuleException("Quotation quantities must be positive.");
        foreach (var line in lines) ServiceInputValidation.RequireDecimalScale(line.Quantity, "Quotation quantity");
        ServiceInputValidation.RequireDecimalScale(billDiscount, "Bill discount");
        notes = ServiceInputValidation.NormalizeOptional(notes);
        ServiceInputValidation.RequireMaxLength(notes, "Quotation notes", 1000);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != productIds.Length) throw new PosRuleException("One or more products no longer exist.");
        if (products.Values.Any(p => !p.IsActive)) throw new PosRuleException("A selected product is inactive.");
        if (customerId is not null && !await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, ct))
            throw new PosRuleException("Customer is not active or does not exist.");

        var saleLines = lines.Select(x =>
        {
            var p = products[x.ProductId];
            var rate = x.DiscountRate ?? p.DiscountRate;
            if (rate is < 0 or > 100) throw new PosRuleException("Item discounts must be between 0 and 100 percent.");
            return new SaleLine(x.Quantity, p.SalePrice, p.PurchasePrice, rate, p.TaxRate);
        }).ToArray();
        var totals = SaleCalculator.Calculate(saleLines, billDiscount, 0);

        var now = DateTime.UtcNow;
        var prefix = await db.Settings.Where(x => x.Key == "QuotePrefix").Select(x => x.Value).SingleOrDefaultAsync(ct) ?? "QT";
        var format = await db.Settings.Where(x => x.Key == "InvoiceNumberFormat").Select(x => x.Value).SingleOrDefaultAsync(ct);
        var quoteNumber = InvoiceNumberFormatter.Format(format, prefix, now.Year,
            await db.Quotations.CountAsync(q => q.QuotedAtUtc.Year == now.Year, ct) + 1);

        DateTime? validUntilUtc = validUntil is null ? null
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(validUntil.Value.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified), TimeZoneInfo.Local);

        var quotation = new Quotation
        {
            QuoteNumber = quoteNumber, CustomerId = customerId, UserId = userId, QuotedAtUtc = now, ValidUntilUtc = validUntilUtc,
            Subtotal = totals.Subtotal, Discount = totals.Discount, BillDiscount = billDiscount, Tax = totals.Tax,
            GrandTotal = totals.GrandTotal, Status = "Open", Notes = notes
        };
        foreach (var x in lines)
        {
            var p = products[x.ProductId];
            var rate = x.DiscountRate ?? p.DiscountRate;
            var gross = x.Quantity * p.SalePrice;
            var lineDiscount = decimal.Round(gross * rate / 100m, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round((gross - lineDiscount) * p.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            quotation.Items.Add(new QuotationItem
            {
                ProductId = p.Id, ProductName = p.Name, Quantity = x.Quantity, UnitPrice = p.SalePrice, DiscountRate = rate,
                Discount = lineDiscount, Tax = tax, Total = decimal.Round(gross - lineDiscount + tax, 2, MidpointRounding.AwayFromZero)
            });
        }
        db.Quotations.Add(quotation);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Create", EntityName = "Quotation", EntityId = quoteNumber, Description = $"Created quotation {quoteNumber} for {totals.GrandTotal:N2}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return quotation;
    }

    public async Task<IReadOnlyList<QuotationSummary>> SearchAsync(int userId, string? query, string? status, int take = 500, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Quote", ct);
        take = Math.Clamp(take, 1, 1000);
        var q = from quote in db.Quotations.AsNoTracking()
                join customer in db.Customers.AsNoTracking() on quote.CustomerId equals (int?)customer.Id into customerRows
                from customer in customerRows.DefaultIfEmpty()
                select new { quote, CustomerName = customer == null ? "Walk-in" : customer.Name };
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.quote.Status == status);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(x => EF.Functions.Like(x.quote.QuoteNumber, $"%{term}%") || EF.Functions.Like(x.CustomerName, $"%{term}%"));
        }
        return await q.OrderByDescending(x => x.quote.QuotedAtUtc).ThenByDescending(x => x.quote.Id).Take(take)
            .Select(x => new QuotationSummary(x.quote.Id, x.quote.QuoteNumber, x.quote.CustomerId, x.CustomerName,
                x.quote.QuotedAtUtc, x.quote.ValidUntilUtc, x.quote.GrandTotal, x.quote.Status, x.quote.ConvertedSaleId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QuotationItemView>> GetItemsAsync(int quotationId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Quote", ct);
        return await db.QuotationItems.AsNoTracking().Where(x => x.QuotationId == quotationId)
            .Select(x => new QuotationItemView(x.ProductId, x.ProductName, x.Quantity, x.UnitPrice, x.DiscountRate, x.Discount, x.Tax, x.Total))
            .ToListAsync(ct);
    }

    public async Task<Sale> ConvertAsync(int quotationId, decimal paid, string paymentMethod, int userId, SaleService sales, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Quote", ct);
        var quotation = await db.Quotations.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == quotationId, ct)
            ?? throw new PosRuleException("This quotation no longer exists.");
        if (!quotation.Status.Equals("Open", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException($"Only open quotations can be converted. This one is {quotation.Status.ToLowerInvariant()}.");
        if (quotation.Items.Count == 0) throw new PosRuleException("This quotation has no items to convert.");

        var cart = quotation.Items.Select(x => (x.ProductId, x.Quantity)).ToArray();
        var lineDiscountRates = quotation.Items.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Last().DiscountRate);
        var sale = await sales.CompleteAsync(cart, userId, quotation.CustomerId, quotation.BillDiscount, paid, paymentMethod,
            quotation.Notes, ct, lineDiscountRates: lineDiscountRates);

        quotation.Status = "Converted"; quotation.ConvertedSaleId = sale.Id; quotation.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Convert", EntityName = "Quotation", EntityId = quotation.QuoteNumber, Description = $"Converted quotation {quotation.QuoteNumber} to sale {sale.InvoiceNumber}." });
        await db.SaveChangesAsync(ct);
        return sale;
    }

    public async Task CancelAsync(int quotationId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Sales.Quote", ct);
        var quotation = await db.Quotations.SingleOrDefaultAsync(x => x.Id == quotationId, ct)
            ?? throw new PosRuleException("This quotation no longer exists.");
        if (quotation.Status.Equals("Converted", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException("A converted quotation cannot be cancelled.");
        quotation.Status = "Cancelled"; quotation.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Cancel", EntityName = "Quotation", EntityId = quotation.QuoteNumber, Description = $"Cancelled quotation {quotation.QuoteNumber}." });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record ShiftSummary(int ShiftId, int UserId, string CashierName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, string Status,
    decimal OpeningFloat, int SaleCount, decimal GrossSales, decimal CashSales, decimal CardSales, decimal CreditSales,
    decimal Refunds, decimal CashExpenses, decimal ExpectedCash, decimal CountedCash, decimal CashVariance);

public sealed record ShiftListRow(int Id, string CashierName, DateTime OpenedAtUtc, DateTime? ClosedAtUtc, string Status,
    decimal OpeningFloat, decimal ExpectedCash, decimal CountedCash, decimal CashVariance);

public sealed class ShiftService(PosDbContext db)
{
    public async Task<Shift> OpenAsync(int userId, decimal openingFloat, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Shift.Manage", ct);
        if (openingFloat < 0) throw new PosRuleException("The opening cash float cannot be negative.");
        ServiceInputValidation.RequireDecimalScale(openingFloat, "Opening float");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Shifts.AnyAsync(x => x.UserId == userId && x.Status == "Open", ct))
            throw new PosRuleException("You already have an open shift. Close it before opening a new one.");
        var shift = new Shift { UserId = userId, OpeningFloat = openingFloat, OpenedAtUtc = DateTime.UtcNow, Status = "Open" };
        db.Shifts.Add(shift);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Open", EntityName = "Shift", EntityId = null, Description = $"Opened shift with {openingFloat:N2} float." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return shift;
    }

    public async Task<Shift?> GetOpenShiftAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Shift.Manage", ct);
        return await db.Shifts.AsNoTracking().Where(x => x.UserId == userId && x.Status == "Open")
            .OrderByDescending(x => x.OpenedAtUtc).FirstOrDefaultAsync(ct);
    }

    public async Task<ShiftSummary> GetSummaryAsync(int shiftId, int userId, CancellationToken ct = default)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Shift.Manage", ct);
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == shiftId, ct)
            ?? throw new PosRuleException("This shift no longer exists.");
        if (shift.UserId != userId && !Permissions.Has(role, "Shift.ViewAll"))
            throw new UnauthorizedAccessException("You can only view your own shift.");
        return await ComputeSummaryAsync(shift, ct);
    }

    public async Task<ShiftSummary> CloseAsync(int shiftId, decimal countedCash, string? notes, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Shift.Manage", ct);
        if (countedCash < 0) throw new PosRuleException("Counted cash cannot be negative.");
        ServiceInputValidation.RequireDecimalScale(countedCash, "Counted cash");
        notes = ServiceInputValidation.NormalizeOptional(notes);
        ServiceInputValidation.RequireMaxLength(notes, "Shift notes", 1000);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var shift = await db.Shifts.SingleOrDefaultAsync(x => x.Id == shiftId && x.UserId == userId, ct)
            ?? throw new PosRuleException("This shift no longer exists or belongs to another user.");
        if (!shift.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)) throw new PosRuleException("This shift is already closed.");
        shift.ClosedAtUtc = DateTime.UtcNow;
        var summary = await ComputeSummaryAsync(shift, ct);
        shift.ExpectedCash = summary.ExpectedCash; shift.CountedCash = countedCash; shift.CashVariance = countedCash - summary.ExpectedCash;
        shift.Status = "Closed"; shift.Notes = notes; shift.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Close", EntityName = "Shift", EntityId = shift.Id.ToString(), Description = $"Closed shift. Expected {shift.ExpectedCash:N2}, counted {countedCash:N2}, variance {shift.CashVariance:N2}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return summary with { ClosedAtUtc = shift.ClosedAtUtc, Status = "Closed", CountedCash = countedCash, CashVariance = shift.CashVariance };
    }

    public async Task<IReadOnlyList<ShiftListRow>> SearchAsync(int userId, int take = 200, CancellationToken ct = default)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Shift.Manage", ct);
        take = Math.Clamp(take, 1, 1000);
        var viewAll = Permissions.Has(role, "Shift.ViewAll");
        var query = from shift in db.Shifts.AsNoTracking()
                    join user in db.Users.AsNoTracking() on shift.UserId equals user.Id into userRows
                    from user in userRows.DefaultIfEmpty()
                    where viewAll || shift.UserId == userId
                    orderby shift.OpenedAtUtc descending
                    select new ShiftListRow(shift.Id, user == null ? "Unknown" : user.DisplayName, shift.OpenedAtUtc, shift.ClosedAtUtc,
                        shift.Status, shift.OpeningFloat, shift.ExpectedCash, shift.CountedCash, shift.CashVariance);
        return await query.Take(take).ToListAsync(ct);
    }

    private async Task<ShiftSummary> ComputeSummaryAsync(Shift shift, CancellationToken ct)
    {
        var from = shift.OpenedAtUtc;
        var to = shift.ClosedAtUtc ?? DateTime.UtcNow;
        var cashierName = await db.Users.AsNoTracking().Where(x => x.Id == shift.UserId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Unknown";
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.UserId == shift.UserId && s.Status != "Voided" && s.SoldAtUtc >= from && s.SoldAtUtc <= to)
            .Select(s => new { s.GrandTotal, s.PaidAmount, s.RemainingAmount, s.CashReceivedAmount })
            .ToListAsync(ct);
        var saleCount = sales.Count;
        var gross = sales.Sum(x => x.GrandTotal);
        var cashSales = sales.Sum(x => x.CashReceivedAmount);
        var cardSales = sales.Sum(x => Math.Max(0, x.PaidAmount - x.CashReceivedAmount));
        var creditSales = sales.Sum(x => x.RemainingAmount);
        var refunds = await db.Returns.AsNoTracking()
            .Where(r => r.UserId == shift.UserId && r.ReturnedAtUtc >= from && r.ReturnedAtUtc <= to).Select(r => r.Total).ToListAsync(ct);
        var refundTotal = refunds.Sum();
        var cashExpenses = await db.Expenses.AsNoTracking()
            .Where(e => e.UserId == shift.UserId && e.PaymentMethod == "Cash" && e.SpentAtUtc >= from && e.SpentAtUtc <= to)
            .Select(e => e.Amount).ToListAsync(ct);
        var cashExpenseTotal = cashExpenses.Sum();
        var expectedCash = shift.OpeningFloat + cashSales - refundTotal - cashExpenseTotal;
        return new ShiftSummary(shift.Id, shift.UserId, cashierName, shift.OpenedAtUtc, shift.ClosedAtUtc, shift.Status,
            shift.OpeningFloat, saleCount, gross, cashSales, cardSales, creditSales, refundTotal, cashExpenseTotal,
            expectedCash, shift.CountedCash, shift.CashVariance);
    }
}
