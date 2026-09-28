using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public void SqliteModelMatchesTheLatestCheckedInMigration()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new PosDbContext(options);

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task EmptyDatabaseIsCreatedFromCheckedInMigrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("20260927104303_InitialSqlite", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927124718_AddCustomerSupplierOpeningBalances", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927133544_AddSupplierPayments", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927134922_AddCustomerAccountPayments", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927142719_AddPurchaseInvoiceBalances", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927144126_AddExpensePaymentDetails", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927151137_AddHeldSaleBillDiscount", await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927155943_AddLoginLockout", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("_AddOperationalQueryIndexes", StringComparison.Ordinal));
        var indexNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) indexNames.Add(reader.GetString(0));
        }
        Assert.Contains("IX_Products_Name", indexNames);
        Assert.Contains("IX_Sales_SoldAtUtc", indexNames);
        Assert.Contains("IX_Purchases_PurchasedAtUtc", indexNames);
        var customer = new SmartPOS.Core.Entities.Customer { Name = "Opening balance migration" , OpeningBalance = 123.45m, Address = "Test address" };
        var supplier = new SmartPOS.Core.Entities.Supplier { Name = "Supplier migration", OpeningBalance = 67.89m };
        db.AddRange(customer, supplier);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(123.45m, (await db.Customers.SingleAsync(x => x.Id == customer.Id)).OpeningBalance);
        Assert.Equal("Test address", (await db.Customers.SingleAsync(x => x.Id == customer.Id)).Address);
        Assert.Equal(67.89m, (await db.Suppliers.SingleAsync(x => x.Id == supplier.Id)).OpeningBalance);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Suppliers.SingleAsync(x => x.Id == supplier.Id)).Company);
    }
}
