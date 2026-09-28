using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class CustomerSupplierMigrationUpgradeTests
{
    [Fact]
    public async Task Upgrade_PreservesExistingCustomerAndSupplierRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);

        await db.Database.MigrateAsync("20260927104303_InitialSqlite");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Customers (Name, Phone, Email, CreditLimit, IsActive, CreatedAtUtc) VALUES ('Existing customer', '123', NULL, 250000, 1, '2026-01-01T00:00:00Z')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Suppliers (Name, Phone, Email, Address, IsActive, CreatedAtUtc) VALUES ('Existing supplier', NULL, NULL, 'Old address', 1, '2026-01-01T00:00:00Z')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (Id, UserName, DisplayName, PasswordHash, Role, IsActive, CreatedAtUtc) VALUES (1, 'legacy-admin', 'Legacy admin', 'hash', 'Administrator', 1, '2026-01-01T00:00:00Z')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Purchases (InvoiceNumber, SupplierId, UserId, PurchasedAtUtc, Total, Status, CreatedAtUtc) VALUES ('OLD-PURCHASE', 1, 1, '2026-01-01T00:00:00Z', 750000, 'Received', '2026-01-01T00:00:00Z')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Expenses (Description, Category, Amount, SpentAtUtc, UserId, CreatedAtUtc) VALUES ('Old utility bill', 'Utilities', 300000, '2026-01-02T00:00:00Z', 1, '2026-01-02T00:00:00Z')");

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var customer = await db.Customers.SingleAsync();
        var supplier = await db.Suppliers.SingleAsync();

        Assert.Equal("Existing customer", customer.Name);
        Assert.Equal("123", customer.Phone);
        Assert.Equal(25m, customer.CreditLimit);
        Assert.Equal(0m, customer.OpeningBalance);
        Assert.Null(customer.Address);
        Assert.Equal("Existing supplier", supplier.Name);
        Assert.Null(supplier.Company);
        Assert.Equal("Old address", supplier.Address);
        Assert.Equal(0m, supplier.OpeningBalance);
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal("OLD-PURCHASE", purchase.InvoiceNumber);
        Assert.Equal(75m, purchase.Total);
        Assert.Equal(0m, purchase.PaidAmount);
        Assert.Equal(75m, purchase.RemainingAmount);
        Assert.Equal("Unpaid", purchase.PaymentStatus);
        var expense = await db.Expenses.SingleAsync();
        Assert.Equal("Old utility bill", expense.Description);
        Assert.Equal(30m, expense.Amount);
        Assert.Equal("Cash", expense.PaymentMethod);
        Assert.Null(expense.Notes);
        var admin = await db.Users.SingleAsync(x => x.Id == 1);
        Assert.Equal(0, admin.FailedLoginAttempts);
        Assert.Null(admin.LockoutUntilUtc);
    }
}
