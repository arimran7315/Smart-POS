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
        await Assert.ThrowsAsync<InvalidOperationException>(() => returns.ReturnAsync(purchase.Id, inventoryManager.Id, [(purchaseItem.Id, 2m)], "Excess return"));
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
