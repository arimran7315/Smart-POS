using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ProductMaximumStockTests
{
    [Fact]
    public async Task PurchaseCannotExceedConfiguredMaximumAndExactMaximumIsAllowed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Capped item", Sku = "CAP-PUR", PurchasePrice = 10, SalePrice = 20, CurrentStock = 4, MaximumStock = 10 };
        db.AddRange(admin, supplier, product);
        await db.SaveChangesAsync();

        var service = new PurchaseService(db);
        await Assert.ThrowsAsync<PosRuleException>(() => service.ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 7m, 10m)]));
        Assert.Equal(0, await db.Purchases.CountAsync());
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(4m, (await db.Products.SingleAsync()).CurrentStock);

        await service.ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 6m, 10m)]);
        Assert.Equal(10m, (await db.Products.SingleAsync()).CurrentStock);
    }

    [Fact]
    public async Task StockAdjustmentEnforcesMaximumButAllowsReductionAndUncappedProducts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var capped = new Product { Name = "Capped item", Sku = "CAP-ADJ", PurchasePrice = 10, SalePrice = 20, CurrentStock = 9, MaximumStock = 10 };
        var uncapped = new Product { Name = "Uncapped item", Sku = "NO-CAP", PurchasePrice = 10, SalePrice = 20, CurrentStock = 0, MaximumStock = 0 };
        db.AddRange(admin, capped, uncapped);
        await db.SaveChangesAsync();

        var service = new InventoryService(db);
        await Assert.ThrowsAsync<PosRuleException>(() => service.AdjustAsync(capped.Id, 2m, admin.Id, "test"));
        Assert.Equal(0, await db.StockMovements.CountAsync());
        await service.AdjustAsync(capped.Id, 1m, admin.Id, "at maximum");
        await service.AdjustAsync(capped.Id, -2m, admin.Id, "reduce stock");
        await service.AdjustAsync(uncapped.Id, 1_000m, admin.Id, "uncapped stock");

        Assert.Equal(8m, (await db.Products.SingleAsync(x => x.Id == capped.Id)).CurrentStock);
        Assert.Equal(1_000m, (await db.Products.SingleAsync(x => x.Id == uncapped.Id)).CurrentStock);
        Assert.Equal(3, await db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task ProductCannotBeSavedWithCurrentStockAboveMaximum()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();

        var product = new Product { Name = "Over limit", Sku = "CAP-SAVE", PurchasePrice = 1, SalePrice = 2, CurrentStock = 11, MaximumStock = 10 };
        await Assert.ThrowsAsync<PosRuleException>(() => new ProductService(db).SaveAsync(product, admin.Id));
        Assert.Equal(0, await db.Products.CountAsync());
    }
}
