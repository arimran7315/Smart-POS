using Microsoft.EntityFrameworkCore;
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
        if (customer.CreditLimit < 0) throw new ArgumentOutOfRangeException(nameof(customer.CreditLimit));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = customer.Id == 0;
        if (created) db.Customers.Add(customer);
        else { var existing = await db.Customers.SingleAsync(x => x.Id == customer.Id, ct); db.Entry(existing).CurrentValues.SetValues(customer); existing.UpdatedAtUtc = DateTime.UtcNow; customer = existing; }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = created ? "Create" : "Update", EntityName = "Customer", EntityId = customer.Id.ToString(), Description = $"{(created ? "Created" : "Updated")} customer {customer.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return customer;
    }
    public async Task<List<Customer>> SearchAsync(string query, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Customers.Manage", ct);
        return await db.Customers.AsNoTracking().Where(x => x.IsActive && (query == "" || x.Name.Contains(query) || x.Phone == query)).OrderBy(x => x.Name).Take(100).ToListAsync(ct);
    }
}

public sealed class SupplierService(PosDbContext db)
{
    public async Task<Supplier> SaveAsync(Supplier supplier, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Suppliers.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplier.Name);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = supplier.Id == 0;
        if (created) db.Suppliers.Add(supplier);
        else { var existing = await db.Suppliers.SingleAsync(x => x.Id == supplier.Id, ct); db.Entry(existing).CurrentValues.SetValues(supplier); existing.UpdatedAtUtc = DateTime.UtcNow; supplier = existing; }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = created ? "Create" : "Update", EntityName = "Supplier", EntityId = supplier.Id.ToString(), Description = $"{(created ? "Created" : "Updated")} supplier {supplier.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return supplier;
    }
    public async Task<List<Supplier>> SearchAsync(string query, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Suppliers.Manage", ct);
        return await db.Suppliers.AsNoTracking().Where(x => x.IsActive && (query == "" || x.Name.Contains(query) || x.Phone == query)).OrderBy(x => x.Name).Take(100).ToListAsync(ct);
    }
}

public sealed class ExpenseService(PosDbContext db)
{
    public async Task<Expense> RecordAsync(string description, string category, decimal amount, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Expenses.Manage", ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Expense amount must be positive.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var expense = new Expense { Description = description.Trim(), Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(), Amount = amount, UserId = userId };
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
        if (!new[] { "Cash", "Card", "Bank transfer" }.Contains(method, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported payment method.", nameof(method));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.SingleAsync(x => x.Id == saleId && x.Status != "Voided", ct);
        if (sale.RemainingAmount <= 0) throw new InvalidOperationException("This sale has no outstanding balance.");
        if (amount > sale.RemainingAmount) throw new InvalidOperationException("Payment exceeds the outstanding balance.");
        var payment = new Payment { SaleId = saleId, Amount = amount, Method = method, Reference = reference?.Trim() };
        sale.PaidAmount += amount; sale.RemainingAmount -= amount; sale.UpdatedAtUtc = DateTime.UtcNow;
        if (sale.RemainingAmount == 0) sale.Status = "Completed";
        db.Payments.Add(payment); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = "Payment", EntityName = "Sale", EntityId = saleId.ToString(), Description = $"Received {amount} against invoice {sale.InvoiceNumber}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return payment;
    }
}

public sealed class UserManagementService(PosDbContext db, PasswordHasher hasher)
{
    private static readonly string[] Roles = ["Administrator", "Manager", "Cashier", "Inventory Manager"];
    public async Task<List<AppUser>> SearchAsync(int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        return await db.Users.AsNoTracking().OrderBy(x => x.UserName).ToListAsync(ct);
    }
    public async Task<AppUser> CreateAsync(string username, string displayName, string password, string role, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        if (!Roles.Contains(role, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown role.", nameof(role));
        ArgumentException.ThrowIfNullOrWhiteSpace(username); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new AppUser { UserName = username.Trim(), DisplayName = displayName.Trim(), PasswordHash = hasher.Hash(password), Role = role };
        db.Users.Add(user); await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Create", EntityName = "User", EntityId = user.Id.ToString(), Description = $"Created user {user.UserName} with role {role}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return user;
    }
    public async Task<AppUser> UpdateAsync(int targetId, string displayName, string role, string? newPassword, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        if (!Roles.Contains(role, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown role.", nameof(role));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (targetId == actorId && !role.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You cannot remove administrator access from your own session.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleAsync(x => x.Id == targetId, ct);
        if (user.IsActive && user.Role == "Administrator" && !role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
            && await db.Users.CountAsync(x => x.Role == "Administrator" && x.IsActive, ct) <= 1)
            throw new InvalidOperationException("The last active administrator cannot be demoted.");
        user.DisplayName = displayName.Trim(); user.Role = role; user.UpdatedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(newPassword)) user.PasswordHash = hasher.Hash(newPassword);
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Update", EntityName = "User", EntityId = user.Id.ToString(), Description = $"Updated account {user.UserName}; role is now {role}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return user;
    }
    public async Task DeactivateAsync(int userId, int actorId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, actorId, "Users.Manage", ct);
        if (userId == actorId) throw new InvalidOperationException("You cannot deactivate your own active session.");
        var target = await db.Users.SingleAsync(x => x.Id == userId, ct);
        if (!target.IsActive) return;
        if (target.Role == "Administrator" && await db.Users.CountAsync(x => x.Role == "Administrator" && x.IsActive, ct) <= 1)
            throw new InvalidOperationException("The last active administrator cannot be deactivated.");
        target.IsActive = false; target.UpdatedAtUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = actorId, Action = "Deactivate", EntityName = "User", EntityId = target.Id.ToString(), Description = $"Deactivated user {target.UserName}." });
        await db.SaveChangesAsync(ct);
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
        var setting = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) db.Settings.Add(new AppSetting { Key = key.Trim(), Value = value });
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
        if (await db.Categories.AnyAsync(x => x.Id != category.Id && x.Name == category.Name.Trim(), ct)) throw new InvalidOperationException("Category name already exists.");
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
        if (await db.Brands.AnyAsync(x => x.Id != brand.Id && x.Name == brand.Name.Trim(), ct)) throw new InvalidOperationException("Brand name already exists.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var create = brand.Id == 0; if (create) db.Brands.Add(brand); else { brand.UpdatedAtUtc = DateTime.UtcNow; db.Brands.Update(brand); }
        await db.SaveChangesAsync(ct);
        db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = create ? "Create" : "Update", EntityName = "Brand", EntityId = brand.Id.ToString(), Description = $"{(create ? "Created" : "Updated")} brand {brand.Name}." });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return brand;
    }
    public Task<List<Category>> CategoriesAsync(CancellationToken ct = default) => db.Categories.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
    public Task<List<Brand>> BrandsAsync(CancellationToken ct = default) => db.Brands.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
}
