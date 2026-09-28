using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Data;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.Infrastructure.Services;

public sealed class CustomerService(PosDbContext db)
{
    public async Task<Customer> SaveAsync(Customer customer, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Customers.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(customer.Name);
        customer.Name = customer.Name.Trim();
        ServiceInputValidation.RequireMaxLength(customer.Name, "Customer name", 200);
        customer.Phone = ServiceInputValidation.NormalizeOptional(customer.Phone);
        customer.Email = ServiceInputValidation.NormalizeOptional(customer.Email);
        customer.Address = ServiceInputValidation.NormalizeOptional(customer.Address);
        ServiceInputValidation.RequireMaxLength(customer.Phone, "Customer phone", 50);
        ServiceInputValidation.RequireMaxLength(customer.Email, "Customer email", 254);
        ServiceInputValidation.RequireMaxLength(customer.Address, "Customer address", 500);
        ServiceInputValidation.RequireDecimalScale(customer.OpeningBalance, "Customer opening balance");
        ServiceInputValidation.RequireDecimalScale(customer.CreditLimit, "Customer credit limit");
        if (customer.CreditLimit < 0) throw new ArgumentOutOfRangeException(nameof(customer.CreditLimit));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var created = customer.Id == 0;
        if (created) db.Customers.Add(customer);
        else
        {
            var existing = await db.Customers.SingleAsync(x => x.Id == customer.Id, ct);
            var savedOpeningBalance = db.Entry(existing).Property(x => x.OpeningBalance).OriginalValue;
            if (customer.OpeningBalance != savedOpeningBalance
                && (await db.Sales.AnyAsync(x => x.CustomerId == customer.Id, ct)
                    || await db.CustomerAccountPayments.AnyAsync(x => x.CustomerId == customer.Id, ct)))
                throw new PosRuleException("Opening balance is locked after account activity begins to preserve the customer ledger.");
            db.Entry(existing).CurrentValues.SetValues(customer); existing.UpdatedAtUtc = DateTime.UtcNow; customer = existing;
        }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = created ? "Create" : "Update", EntityName = "Customer", EntityId = customer.Id.ToString(), Description = $"{(created ? "Created" : "Updated")} customer {customer.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return customer;
    }
    public async Task<List<Customer>> SearchAsync(string query, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAnyAsync(db, userId, ct, "Customers.View", "Customers.Manage");
        return await db.Customers.AsNoTracking().Where(x => x.IsActive && (query == "" || x.Name.Contains(query) || x.Phone == query)).OrderBy(x => x.Name).Take(100).ToListAsync(ct);
    }
}

public sealed class SupplierService(PosDbContext db)
{
    public async Task<Supplier> SaveAsync(Supplier supplier, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Suppliers.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplier.Name);
        supplier.Name = supplier.Name.Trim();
        ServiceInputValidation.RequireMaxLength(supplier.Name, "Supplier name", 200);
        ServiceInputValidation.RequireMaxLength(supplier.Company, "Supplier company", 200);
        supplier.Phone = ServiceInputValidation.NormalizeOptional(supplier.Phone);
        supplier.Email = ServiceInputValidation.NormalizeOptional(supplier.Email);
        supplier.Address = ServiceInputValidation.NormalizeOptional(supplier.Address);
        ServiceInputValidation.RequireMaxLength(supplier.Phone, "Supplier phone", 50);
        ServiceInputValidation.RequireMaxLength(supplier.Email, "Supplier email", 254);
        ServiceInputValidation.RequireMaxLength(supplier.Address, "Supplier address", 500);
        ServiceInputValidation.RequireDecimalScale(supplier.OpeningBalance, "Supplier opening balance");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var created = supplier.Id == 0;
        if (created) db.Suppliers.Add(supplier);
        else
        {
            var existing = await db.Suppliers.SingleAsync(x => x.Id == supplier.Id, ct);
            var savedOpeningBalance = db.Entry(existing).Property(x => x.OpeningBalance).OriginalValue;
            if (supplier.OpeningBalance != savedOpeningBalance
                && (await db.Purchases.AnyAsync(x => x.SupplierId == supplier.Id, ct)
                    || await db.SupplierPayments.AnyAsync(x => x.SupplierId == supplier.Id, ct)))
                throw new PosRuleException("Opening balance is locked after account activity begins to preserve the supplier ledger.");
            db.Entry(existing).CurrentValues.SetValues(supplier); existing.UpdatedAtUtc = DateTime.UtcNow; supplier = existing;
        }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = created ? "Create" : "Update", EntityName = "Supplier", EntityId = supplier.Id.ToString(), Description = $"{(created ? "Created" : "Updated")} supplier {supplier.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return supplier;
    }
    public async Task<List<Supplier>> SearchAsync(string query, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Suppliers.Manage", ct);
        return await db.Suppliers.AsNoTracking().Where(x => x.IsActive && (query == "" || x.Name.Contains(query) || (x.Company != null && x.Company.Contains(query)) || x.Phone == query)).OrderBy(x => x.Name).Take(100).ToListAsync(ct);
    }
}

public sealed class ExpenseService(PosDbContext db)
{
    public async Task<Expense> RecordAsync(string description, string category, decimal amount, int userId, CancellationToken ct = default,
        string paymentMethod = "Cash", string? notes = null, DateOnly? spentOn = null)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Expenses.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        description = description.Trim();
        category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
        notes = ServiceInputValidation.NormalizeOptional(notes);
        ServiceInputValidation.RequireMaxLength(description, "Expense description", 250);
        ServiceInputValidation.RequireMaxLength(category, "Expense category", 100);
        ServiceInputValidation.RequireMaxLength(notes, "Expense notes", 1000);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Expense amount must be positive.");
        ServiceInputValidation.RequireDecimalScale(amount, "Expense amount");
        if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(paymentMethod, StringComparer.OrdinalIgnoreCase))
            throw new PosRuleException("Choose Cash, Card, or Bank transfer as the expense payment method.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var localDate = (spentOn ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(new TimeOnly(12, 0));
        var spentAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var expense = new Expense { Description = description, Category = category, Amount = amount,
            PaymentMethod = paymentMethod, Notes = notes, SpentAtUtc = spentAtUtc, UserId = userId };
        db.Expenses.Add(expense); await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Create", EntityName = "Expense", EntityId = expense.Id.ToString(), Description = $"Recorded expense {expense.Amount}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return expense;
    }
}

public sealed class PaymentService(PosDbContext db)
{
    public async Task<Payment> ReceiveCreditPaymentAsync(int saleId, decimal amount, int userId, string method, string? reference = null, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Receive", ct);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        ServiceInputValidation.RequireDecimalScale(amount, "Payment amount");
        if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(method, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported payment method.", nameof(method));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sale = await db.Sales.SingleAsync(x => x.Id == saleId && x.Status != "Voided", ct);
        if (sale.RemainingAmount <= 0) throw new PosRuleException("This sale has no outstanding balance.");
        if (amount > sale.RemainingAmount) throw new PosRuleException("Payment exceeds the outstanding balance.");
        var payment = new Payment { SaleId = saleId, Amount = amount, Method = method, Reference = reference?.Trim() };
        sale.PaidAmount += amount; sale.RemainingAmount -= amount; sale.UpdatedAtUtc = DateTime.UtcNow;
        if (sale.RemainingAmount == 0) sale.Status = "Completed";
        db.Payments.Add(payment); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Sale", EntityId = saleId.ToString(), Description = $"Received {amount} against invoice {sale.InvoiceNumber}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return payment;
    }
}

public sealed class CustomerAccountPaymentService(PosDbContext db)
{
    public async Task<CustomerAccountPayment> ReceiveAsync(int customerId, decimal amount, int userId, string method, string? reference = null, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Receive", ct);
        if (amount <= 0) throw new PosRuleException("Payment amount must be greater than zero.");
        ServiceInputValidation.RequireDecimalScale(amount, "Payment amount");
        if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(method, StringComparer.OrdinalIgnoreCase))
            throw new PosRuleException("Unsupported payment method.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == customerId && x.IsActive, ct)
            ?? throw new PosRuleException("Customer is inactive or no longer exists.");
        var invoiceBalances = await db.Sales.Where(x => x.CustomerId == customerId && x.Status != "Voided" && x.RemainingAmount > 0)
            .Select(x => x.RemainingAmount).ToListAsync(ct);
        var received = await db.CustomerAccountPayments.Where(x => x.CustomerId == customerId).Select(x => x.Amount).ToListAsync(ct);
        var outstanding = customer.OpeningBalance + invoiceBalances.Sum() - received.Sum();
        if (outstanding <= 0) throw new PosRuleException("This customer has no outstanding account balance.");
        if (amount > outstanding) throw new PosRuleException("Payment exceeds the customer's outstanding account balance.");
        var payment = new CustomerAccountPayment { CustomerId = customerId, UserId = userId, Amount = amount, Method = method, Reference = reference?.Trim() };
        db.CustomerAccountPayments.Add(payment);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Customer", EntityId = customerId.ToString(), Description = $"Received account payment {amount:N2} by {method}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return payment;
    }
}

public sealed class SupplierPaymentService(PosDbContext db)
{
    public async Task<IReadOnlyList<OutstandingPurchasePayment>> SearchOpenPurchasesAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Make", ct);
        return await (from purchase in db.Purchases.AsNoTracking()
                      join supplier in db.Suppliers.AsNoTracking() on purchase.SupplierId equals supplier.Id
                      where purchase.RemainingAmount > 0 && purchase.Status != "Returned"
                      orderby purchase.PurchasedAtUtc
                      select new OutstandingPurchasePayment(purchase.Id, purchase.InvoiceNumber, supplier.Name,
                          purchase.PurchasedAtUtc, purchase.Total, purchase.PaidAmount, purchase.RemainingAmount, purchase.PaymentStatus))
            .Take(500).ToListAsync(ct);
    }

    public async Task<SupplierPayment> RecordForPurchaseAsync(int purchaseId, decimal amount, int userId, string method, string? reference = null, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Make", ct);
        Validate(amount, method);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var purchase = await db.Purchases.SingleOrDefaultAsync(x => x.Id == purchaseId, ct)
            ?? throw new PosRuleException("Purchase was not found.");
        if (purchase.Status == "Returned" || purchase.RemainingAmount <= 0)
            throw new PosRuleException("This purchase has no outstanding amount to pay.");
        if (amount > purchase.RemainingAmount)
            throw new PosRuleException($"Payment exceeds the outstanding invoice amount of {purchase.RemainingAmount:N2}.");
        if (!await db.Suppliers.AnyAsync(x => x.Id == purchase.SupplierId, ct))
            throw new PosRuleException("The purchase supplier no longer exists.");
        purchase.PaidAmount += amount;
        purchase.RemainingAmount -= amount;
        purchase.PaymentStatus = purchase.RemainingAmount == 0 ? "Paid" : "Partial";
        purchase.UpdatedAtUtc = DateTime.UtcNow;
        var payment = new SupplierPayment { SupplierId = purchase.SupplierId, PurchaseId = purchase.Id, UserId = userId, Amount = amount, Method = method, Reference = reference?.Trim() };
        db.SupplierPayments.Add(payment);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Purchase", EntityId = purchase.InvoiceNumber, Description = $"Paid {amount:N2} toward purchase invoice; {purchase.RemainingAmount:N2} remains." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return payment;
    }

    public async Task<SupplierPayment> RecordAsync(int supplierId, decimal amount, int userId, string method, string? reference = null, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Make", ct);
        Validate(amount, method);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == supplierId && x.IsActive, ct)
            ?? throw new PosRuleException("Supplier is inactive or no longer exists.");
        var payment = new SupplierPayment { SupplierId = supplierId, UserId = userId, Amount = amount, Method = method, Reference = reference?.Trim() };
        db.SupplierPayments.Add(payment);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Supplier", EntityId = supplierId.ToString(), Description = $"Recorded supplier payment {amount:N2} by {method}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return payment;
    }

    private static void Validate(decimal amount, string method)
    {
        if (amount <= 0) throw new PosRuleException("Supplier payment amount must be greater than zero.");
        ServiceInputValidation.RequireDecimalScale(amount, "Supplier payment amount");
        if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(method, StringComparer.OrdinalIgnoreCase))
            throw new PosRuleException("Unsupported supplier payment method.");
    }
}

public sealed record OutstandingPurchasePayment(int PurchaseId, string InvoiceNumber, string SupplierName,
    DateTime PurchasedAtUtc, decimal Total, decimal PaidAmount, decimal RemainingAmount, string PaymentStatus);

public sealed record UserAccountSummary(int Id, string UserName, string DisplayName, string Role, bool IsActive, DateTime CreatedAtUtc);

public sealed class UserManagementService(PosDbContext db, PasswordHasher hasher)
{
    private static readonly string[] Roles = ["Administrator", "Manager", "Cashier", "Inventory Manager"];
    public async Task<List<UserAccountSummary>> SearchAsync(int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        return await db.Users.AsNoTracking().OrderBy(x => x.UserName)
            .Select(x => new UserAccountSummary(x.Id, x.UserName, x.DisplayName, x.Role, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(ct);
    }
    public async Task<AppUser> CreateAsync(string username, string displayName, string password, string role, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        role = NormalizeRole(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(username); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        username = username.Trim(); displayName = displayName.Trim();
        ServiceInputValidation.RequireMaxLength(username, "Username", 64);
        ServiceInputValidation.RequireMaxLength(displayName, "Display name", 200);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new AppUser { UserName = username, DisplayName = displayName, PasswordHash = hasher.Hash(password), Role = role };
        db.Users.Add(user); await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Create", EntityName = "User", EntityId = user.Id.ToString(), Description = $"Created user {user.UserName} with role {role}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return user;
    }
    public async Task<AppUser> UpdateAsync(int targetId, string displayName, string role, string? newPassword, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        role = NormalizeRole(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        displayName = displayName.Trim();
        ServiceInputValidation.RequireMaxLength(displayName, "Display name", 200);
        if (targetId == actorId && !role.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException("You cannot remove administrator access from your own session.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.SingleAsync(x => x.Id == targetId, ct);
        if (user.IsActive && IsAdministrator(user.Role) && !IsAdministrator(role)
            && await CountActiveAdministratorsAsync(ct) <= 1)
            throw new PosRuleException("The last active administrator cannot be demoted.");
        user.DisplayName = displayName; user.Role = role; user.UpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(newPassword)) user.PasswordHash = hasher.Hash(newPassword);
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Update", EntityName = "User", EntityId = user.Id.ToString(), Description = $"Updated account {user.UserName}; role is now {role}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return user;
    }
    public async Task DeactivateAsync(int userId, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        if (userId == actorId) throw new PosRuleException("You cannot deactivate your own active session.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var target = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (!target.IsActive) return;
        if (IsAdministrator(target.Role) && await CountActiveAdministratorsAsync(ct) <= 1)
            throw new PosRuleException("The last active administrator cannot be deactivated.");
        target.IsActive = false; target.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Deactivate", EntityName = "User", EntityId = target.Id.ToString(), Description = $"Deactivated user {target.UserName}." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string NormalizeRole(string role)
    {
        var normalized = Roles.FirstOrDefault(x => x.Equals(role?.Trim(), StringComparison.OrdinalIgnoreCase));
        return normalized ?? throw new ArgumentException("Unknown role.", nameof(role));
    }

    private static bool IsAdministrator(string role) => role.Equals("Administrator", StringComparison.OrdinalIgnoreCase);

    private async Task<int> CountActiveAdministratorsAsync(CancellationToken ct)
    {
        var roles = await db.Users.AsNoTracking().Where(x => x.IsActive).Select(x => x.Role).ToListAsync(ct);
        return roles.Count(IsAdministrator);
    }
}

public sealed class SettingsService(PosDbContext db)
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Settings.View", ct);
        return await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, ct);
    }
    public async Task SetAsync(string key, string value, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Settings.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        key = key.Trim();
        value = value?.Trim() ?? "";
        var supportedKeys = new[]
        {
            "ApplicationName", "StoreName", "StoreAddress", "StorePhone", "StoreEmail", "TaxNumber", "Currency",
            "ReceiptWidthMm", "DefaultPrinter", "ReceiptPrintMode", "InvoicePrefix", "PurchasePrefix", "PurchaseReturnPrefix",
            "InvoiceNumberFormat", "LowStockThreshold", "Theme", "BackupLocation"
        };
        var canonicalKey = supportedKeys.FirstOrDefault(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (canonicalKey is null)
            throw new PosRuleException("This setting is not editable. Choose a supported store or system setting.");
        key = canonicalKey;
        if (value.Length > 500) throw new PosRuleException("Setting values must be 500 characters or fewer.");
        if (key is "StoreName" or "StoreAddress" or "StorePhone" or "StoreEmail" or "TaxNumber" or "Currency"
            or "DefaultPrinter" or "InvoicePrefix" or "PurchasePrefix" or "PurchaseReturnPrefix" or "BackupLocation")
        {
            if (value.Length == 0 && (key is "StoreName" or "Currency" or "InvoicePrefix" or "PurchasePrefix" or "PurchaseReturnPrefix"))
                throw new PosRuleException($"{key} cannot be empty.");
            if (value.Any(char.IsControl)) throw new PosRuleException("Setting values cannot contain control characters.");
        }
        if (key == "ReceiptWidthMm" && value is not ("58" or "80"))
            throw new PosRuleException("Receipt width must be 58 or 80 millimeters.");
        if (key == "ReceiptPrintMode" && !value.Equals("Dialog", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("EscPos", StringComparison.OrdinalIgnoreCase))
            throw new PosRuleException("Receipt print mode must be Dialog or EscPos.");
        if (key == "ReceiptPrintMode") value = value.Equals("EscPos", StringComparison.OrdinalIgnoreCase) ? "EscPos" : "Dialog";
        if ((key is "InvoicePrefix" or "PurchasePrefix" or "PurchaseReturnPrefix") && (value.Length > 32 || value.Any(char.IsControl)))
            throw new PosRuleException("Invoice prefixes must contain at most 32 printable characters.");
        if (key.Equals("Theme", StringComparison.OrdinalIgnoreCase))
        {
            key = "Theme";
            if (value.Equals("Light", StringComparison.OrdinalIgnoreCase)) value = "Light";
            else if (value.Equals("Dark", StringComparison.OrdinalIgnoreCase)) value = "Dark";
            else throw new PosRuleException("Theme must be set to Light or Dark.");
        }
        if (key.Equals("ApplicationName", StringComparison.OrdinalIgnoreCase))
        {
            key = "ApplicationName";
            if (value.Length is 0 or > 64) throw new PosRuleException("Application name must contain 1 to 64 characters.");
        }
        if (key.Equals("LowStockThreshold", StringComparison.OrdinalIgnoreCase))
        {
            key = "LowStockThreshold";
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) || threshold < 0)
                throw new PosRuleException("Low stock threshold must be a non-negative number using a period for decimals.");
            value = threshold.ToString("0.############################", CultureInfo.InvariantCulture);
        }
        if (key.Equals("InvoiceNumberFormat", StringComparison.OrdinalIgnoreCase))
        {
            key = "InvoiceNumberFormat";
            try { _ = InvoiceNumberFormatter.Format(value, "TEST", 2026, 1); }
            catch (FormatException ex) { throw new PosRuleException(ex.Message); }
        }
        var setting = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else { setting.Value = value; setting.UpdatedAtUtc = DateTime.UtcNow; }
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Update", EntityName = "Setting", EntityId = key, Description = $"Updated setting {key}." });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class AuditService(PosDbContext db)
{
    public async Task<List<AuditEntry>> RecentAsync(int userId, int take = 200, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Audit.View", ct);
        take = Math.Clamp(take, 1, 1000);
        return await db.AuditEntries.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(take).ToListAsync(ct);
    }
}

public sealed class CatalogTaxonomyService(PosDbContext db)
{
    public async Task<Category> SaveCategoryAsync(Category category, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(category.Name);
        category.Name = category.Name.Trim();
        ServiceInputValidation.RequireMaxLength(category.Name, "Category name", 100);
        if (await db.Categories.AnyAsync(x => x.Id != category.Id && x.Name == category.Name.Trim(), ct)) throw new PosRuleException("Category name already exists.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var create = category.Id == 0; if (create) db.Categories.Add(category); else { category.UpdatedAtUtc = DateTime.UtcNow; db.Categories.Update(category); }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = create ? "Create" : "Update", EntityName = "Category", EntityId = category.Id.ToString(), Description = $"{(create ? "Created" : "Updated")} category {category.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return category;
    }
    public async Task<Brand> SaveBrandAsync(Brand brand, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(brand.Name);
        brand.Name = brand.Name.Trim();
        ServiceInputValidation.RequireMaxLength(brand.Name, "Brand name", 100);
        if (await db.Brands.AnyAsync(x => x.Id != brand.Id && x.Name == brand.Name.Trim(), ct)) throw new PosRuleException("Brand name already exists.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var create = brand.Id == 0; if (create) db.Brands.Add(brand); else { brand.UpdatedAtUtc = DateTime.UtcNow; db.Brands.Update(brand); }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = create ? "Create" : "Update", EntityName = "Brand", EntityId = brand.Id.ToString(), Description = $"{(create ? "Created" : "Updated")} brand {brand.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return brand;
    }
    public async Task DeactivateCategoryAsync(int categoryId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        var category = await db.Categories.SingleAsync(x => x.Id == categoryId, ct);
        if (!category.IsActive) return;
        category.IsActive = false; category.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Deactivate", EntityName = "Category", EntityId = categoryId.ToString(), Description = $"Deactivated category {category.Name}. Existing products retain the category link." });
        await db.SaveChangesAsync(ct);
    }
    public async Task DeactivateBrandAsync(int brandId, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Products.Manage", ct);
        var brand = await db.Brands.SingleAsync(x => x.Id == brandId, ct);
        if (!brand.IsActive) return;
        brand.IsActive = false; brand.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Deactivate", EntityName = "Brand", EntityId = brandId.ToString(), Description = $"Deactivated brand {brand.Name}. Existing products retain the brand link." });
        await db.SaveChangesAsync(ct);
    }
    public async Task<List<Category>> CategoriesAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAnyAsync(db, userId, ct, "Products.View", "Products.Manage");
        return await db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
    }
    public async Task<List<Brand>> BrandsAsync(int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAnyAsync(db, userId, ct, "Products.View", "Products.Manage");
        return await db.Brands.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
    }
}
