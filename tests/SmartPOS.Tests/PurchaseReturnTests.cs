using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PurchaseReturnTests
{
    [Fact]
    public async Task PurchaseDiscountAndTaxPersistAndReturnsRefundNetLineValue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var stockManager = new AppUser { UserName = "stock", DisplayName = "Stock", PasswordHash = "test", Role = "Inventory Manager" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Cable", Sku = "DISC-TAX", PurchasePrice = 20, SalePrice = 35 };
        db.AddRange(admin, stockManager, supplier, product);
        await db.SaveChangesAsync();

        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id,
            [new PurchaseLineInput(product.Id, 3m, 20m, 10m, 5m)]);
        var item = Assert.Single(purchase.Items);
        Assert.Equal(6m, item.Discount);
        Assert.Equal(2.7m, item.Tax);
        Assert.Equal(56.7m, purchase.Total);
        Assert.Equal(18m, product.PurchasePrice);

        db.ChangeTracker.Clear();
        var persisted = await db.Purchases.Include(x => x.Items).SingleAsync(x => x.Id == purchase.Id);
        Assert.Equal(6m, persisted.Discount);
        Assert.Equal(2.7m, persisted.Tax);
        Assert.Equal(56.7m, persisted.Items.Single().Total);

        var returns = new PurchaseReturnsService(db);
        await Assert.ThrowsAsync<PosRuleException>(() => returns.ReturnAsync(purchase.Id, stockManager.Id, [(item.Id, 1m)], " "));
        var first = await returns.ReturnAsync(purchase.Id, stockManager.Id, [(item.Id, 1m)], "Damaged");
        Assert.Equal(18.9m, first.Total);
        var final = await returns.ReturnAsync(purchase.Id, stockManager.Id, [(item.Id, 2m)], "Remaining");
        Assert.Equal(37.8m, final.Total);
        Assert.Equal(56.7m, first.Total + final.Total);

        var fractionalPurchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 3m, 3.3333333333m)]);
        var fractionalItem = Assert.Single(fractionalPurchase.Items);
        Assert.Equal(10m, fractionalPurchase.Total);
        var firstUnit = await returns.ReturnAsync(fractionalPurchase.Id, stockManager.Id, [(fractionalItem.Id, 1m)], "Partial one");
        var remaining = Assert.Single(await returns.GetReturnableItemsAsync(fractionalPurchase.Id, stockManager.Id));
        Assert.Equal(2m, remaining.QuantityRemaining);
        Assert.Equal(6.67m, remaining.ValueRemaining);
        var secondUnit = await returns.ReturnAsync(fractionalPurchase.Id, stockManager.Id, [(fractionalItem.Id, 1m)], "Partial two");
        var lastUnit = await returns.ReturnAsync(fractionalPurchase.Id, stockManager.Id, [(fractionalItem.Id, 1m)], "Final unit");
        Assert.Equal(3.33m, firstUnit.Total);
        Assert.Equal(3.34m, secondUnit.Total);
        Assert.Equal(3.33m, lastUnit.Total);
        Assert.Equal(10m, firstUnit.Total + secondUnit.Total + lastUnit.Total);
    }

    [Fact]
    public async Task PurchaseReturnReducesStockAndCannotExceedReceivedQuantity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var inventoryManager = new AppUser { UserName = "stock", DisplayName = "Stock manager", PasswordHash = "test-hash", Role = "Inventory Manager" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test-hash", Role = "Cashier" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Cable", Sku = "CB-1", PurchasePrice = 20, SalePrice = 35 };
        db.AddRange(admin, inventoryManager, cashier, supplier, product);
        await db.SaveChangesAsync();
        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 3m, 20m)]);
        var purchaseItem = Assert.Single(purchase.Items);
        var returns = new PurchaseReturnsService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => returns.SearchPurchasesAsync(cashier.Id));
        var first = await returns.ReturnAsync(purchase.Id, inventoryManager.Id, [(purchaseItem.Id, 1.5m)], "Damaged packaging");
        Assert.Equal(30m, first.Total);
        Assert.Equal(1.5m, product.CurrentStock);
        Assert.Equal("PartiallyReturned", purchase.Status);
        await Assert.ThrowsAsync<PosRuleException>(() => returns.ReturnAsync(purchase.Id, inventoryManager.Id, [(purchaseItem.Id, 2m)], "Excess return"));
        Assert.Equal(1.5m, product.CurrentStock);

        await returns.ReturnAsync(purchase.Id, inventoryManager.Id, [(purchaseItem.Id, 1.5m)], "Remaining stock");
        Assert.Equal(0m, product.CurrentStock);
        Assert.Equal("Returned", purchase.Status);
        Assert.Equal(2, await db.PurchaseReturns.CountAsync());
        Assert.Equal(2, await db.StockMovements.CountAsync(x => x.Reason == "PurchaseReturn"));
        Assert.False(Permissions.Has("Cashier", "Purchases.Return"));
        Assert.True(Permissions.Has("Inventory Manager", "Purchases.Return"));
    }
}
