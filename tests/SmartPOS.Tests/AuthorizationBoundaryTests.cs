using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class AuthorizationBoundaryTests
{
    [Fact]
    public async Task CashierCannotReachProtectedWorkflowsThroughServicesDirectly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "permission-cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        var product = new ProductService(db);
        var inventory = new InventoryService(db);
        var sales = new SaleService(db);
        var purchases = new PurchaseService(db);
        var reports = new ReportsService(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => product.SaveAsync(new Product { Name = "No", Sku = "NO" }, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => product.DeactivateAsync(999, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => product.RecordBarcodeLabelsPrintedAsync(999, 1, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.AdjustAsync(999, 1, cashier.Id, "No"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchases.ReceiveAsync(999, cashier.Id, [(999, 1m, 1m)]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.VoidAsync(999, cashier.Id, "No"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ReturnsService(db).ReturnAsync(999, cashier.Id, [(999, 1m)], "No"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PurchaseReturnsService(db).SearchPurchasesAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExpenseService(db).RecordAsync("No", "No", 1, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PaymentService(db).ReceiveCreditPaymentAsync(999, 1, cashier.Id, "Cash"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CustomerAccountPaymentService(db).ReceiveAsync(999, 1, cashier.Id, "Cash"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SupplierPaymentService(db).RecordAsync(999, 1, cashier.Id, "Cash"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CustomerService(db).SaveAsync(new Customer { Name = "No" }, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SupplierService(db).SaveAsync(new Supplier { Name = "No" }, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SettingsService(db).SetAsync("StoreName", "Tampered", cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new UserManagementService(db, new PasswordHasher()).SearchAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new AuditService(db).RecentAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.GetDailyProfitAsync(cashier.Id, today, today));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DatabaseBackupService(db).CreateBackupAsync(Path.Combine(Path.GetTempPath(), "forbidden-smartpos-backup.db"), cashier.Id));

        Assert.Empty(await db.Products.ToListAsync());
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.Purchases.ToListAsync());
        Assert.Empty(await db.Expenses.ToListAsync());
        Assert.Empty(await db.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task ManagerCannotIncludeAdministratorOnlyPaymentWithPurchaseReceipt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var manager = new AppUser { UserName = "payment-manager", DisplayName = "Manager", PasswordHash = "test", Role = "Manager" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Stock item", Sku = "STOCK-1", CurrentStock = 4, PurchasePrice = 5, SalePrice = 10 };
        db.AddRange(manager, supplier, product);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PurchaseService(db).ReceiveWithPaymentAsync(
            supplier.Id, manager.Id, [new PurchaseLineInput(product.Id, 2, 5)], amountPaid: 5, paymentMethod: "Cash"));

        Assert.Empty(await db.Purchases.ToListAsync());
        Assert.Empty(await db.SupplierPayments.ToListAsync());
        Assert.Equal(4m, (await db.Products.SingleAsync(x => x.Id == product.Id)).CurrentStock);
    }
}
