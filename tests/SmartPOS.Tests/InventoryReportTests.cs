using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class InventoryReportTests
{
    [Fact]
    public async Task GlobalLowStockThresholdAppliesOnlyWhenProductHasNoSpecificMinimum()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var defaulted = new Product { Name = "Default threshold", Sku = "THRESH-DEFAULT", CurrentStock = 4 };
        var configured = new Product { Name = "Specific minimum", Sku = "THRESH-SPECIFIC", CurrentStock = 3, MinimumStock = 2 };
        db.AddRange(admin, defaulted, configured);
        await db.SaveChangesAsync();
        await new SettingsService(db).SetAsync("lowstockthreshold", "5", admin.Id);

        var products = await new ProductService(db).SearchAsync("", admin.Id, lowStockOnly: true);
        Assert.Equal("THRESH-DEFAULT", Assert.Single(products).Sku);
        var reportRow = Assert.Single(await new ReportsService(db).GetLowStockAsync(admin.Id));
        Assert.Equal(5m, reportRow.MinimumStock);
        Assert.Equal(1m, reportRow.ReorderQuantity);
        Assert.Equal(1, (await new DashboardService(db).GetTodayAsync(admin.Id)).LowStockCount);
    }

    [Fact]
    public async Task ValuationUsesCurrentStockAndPurchaseCostAndLowStockUsesProductMinimum()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var category = new Category { Name = "Grocery" };
        var low = new Product { Name = "Low item", Sku = "LOW-1", Category = category, Unit = "box", CurrentStock = 3, MinimumStock = 5, MaximumStock = 20, PurchasePrice = 12.5m, SalePrice = 20 };
        var healthy = new Product { Name = "Healthy item", Sku = "OK-1", CurrentStock = 10, MinimumStock = 2, PurchasePrice = 4, SalePrice = 8 };
        var inactive = new Product { Name = "Inactive item", Sku = "OFF-1", IsActive = false, CurrentStock = 0, MinimumStock = 1 };
        db.AddRange(admin, category, low, healthy, inactive);
        await db.SaveChangesAsync();

        var reports = new ReportsService(db);
        var valuation = await reports.GetInventoryValuationAsync(admin.Id);
        Assert.Equal(2, valuation.Count);
        var lowValue = Assert.Single(valuation, x => x.Sku == "LOW-1");
        Assert.Equal("Grocery", lowValue.Category);
        Assert.Equal(37.5m, lowValue.EstimatedStockValue);
        Assert.Equal(12.5m, lowValue.PurchaseCost);

        var lowStock = await reports.GetLowStockAsync(admin.Id);
        var lowRow = Assert.Single(lowStock);
        Assert.Equal("LOW-1", lowRow.Sku);
        Assert.Equal(2m, lowRow.ReorderQuantity);
    }

    [Fact]
    public async Task InventoryReportsRequireReportsPermission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        await db.Users.AddAsync(cashier);
        await db.SaveChangesAsync();

        var reports = new ReportsService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.GetInventoryValuationAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.GetLowStockAsync(cashier.Id));
    }
}
