using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class InventoryMovementReconciliationTests
{
    [Fact]
    public async Task EveryStockWorkflowReconcilesProductBalanceToMovementLedger()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var admin = new AppUser { UserName = "inventory-owner", DisplayName = "Owner", PasswordHash = "hash", Role = "Administrator" };
        var supplier = new Supplier { Name = "Movement supplier" };
        db.AddRange(admin, supplier);
        await db.SaveChangesAsync();

        var products = new ProductService(db);
        var product = await products.SaveAsync(new Product
        {
            Name = "Movement test item", Sku = "MOVEMENT-1", PurchasePrice = 4m,
            SalePrice = 10m, CurrentStock = 10m
        }, admin.Id);
        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 5m, 4m)]);
        var sale = await new SaleService(db).CompleteAsync([(product.Id, 2m)], admin.Id,
            customerId: null, billDiscount: 0m, paid: 20m, paymentMethod: "Cash", notes: null);
        await new ReturnsService(db).ReturnAsync(sale.Id, admin.Id, [(sale.Items[0].Id, 1m)], "Customer return");
        await new PurchaseReturnsService(db).ReturnAsync(purchase.Id, admin.Id,
            [(purchase.Items[0].Id, 1m)], "Supplier return");
        await new InventoryService(db).AdjustAsync(product.Id, -2m, admin.Id, "Damaged stock");

        var movements = await db.StockMovements.AsNoTracking().Where(x => x.ProductId == product.Id)
            .OrderBy(x => x.Id).ToListAsync();
        var savedStock = await db.Products.AsNoTracking().Where(x => x.Id == product.Id)
            .Select(x => x.CurrentStock).SingleAsync();
        var expectedDeltas = new[] { 10m, 5m, -2m, 1m, -1m, -2m };
        var expectedReasons = new[] { "OpeningStock", "Purchase", "Sale", "SaleReturn", "PurchaseReturn", "Damaged stock" };

        Assert.Equal(expectedDeltas, movements.Select(x => x.QuantityDelta));
        Assert.Equal(expectedReasons, movements.Select(x => x.Reason));
        decimal balance = 0m;
        foreach (var movement in movements)
        {
            balance += movement.QuantityDelta;
            Assert.Equal(balance, movement.BalanceAfter);
        }
        Assert.Equal(11m, savedStock);
        Assert.Equal(savedStock, movements.Sum(x => x.QuantityDelta));
        Assert.Equal(savedStock, balance);
    }
}
