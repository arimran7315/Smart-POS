using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SqlServerMigrationTests
{
    [Fact]
    public void SqlServerProviderDiscoversItsOwnInitialSchemaWithoutConnecting()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlServer("Server=localhost;Database=SmartPOSMigrationTest;User Id=unused;Password=unused;TrustServerCertificate=True",
                sql => sql.MigrationsAssembly("SmartPOS.SqlServerMigrations"))
            .Options;
        using var db = new PosDbContext(options);

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20260927214634_InitialSqlServer", db.Database.GetMigrations());
        Assert.Contains("CREATE TABLE [Sales]", db.Database.GenerateCreateScript());

        var migrationScript = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [Sales]", migrationScript);
        Assert.Contains("ALTER TABLE [Products] ADD [StockVersion]", migrationScript);
        Assert.Contains("IX_Products_Name", migrationScript);
        Assert.Contains("IX_Sales_SoldAtUtc", migrationScript);
        Assert.Contains("IX_Purchases_PurchasedAtUtc", migrationScript);
        Assert.Contains("DATALENGTH([Name]) > 400", migrationScript);
        Assert.Contains(db.Database.GetMigrations(), migration => migration.EndsWith("_AddSupplierCompany", StringComparison.Ordinal));
        Assert.Contains("ALTER TABLE [Suppliers] ADD [Company]", migrationScript);
    }
}
