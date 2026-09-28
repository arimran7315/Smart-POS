using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class HeldSaleTests
{
    [Fact]
    public async Task CancellingSaleRemovesOwnedHoldAndAuditsBothHeldAndDraftCancellations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var other = new AppUser { UserName = "other", DisplayName = "Other", PasswordHash = "hash", Role = "Cashier" };
        var product = new Product { Name = "Coffee", Sku = "CANCEL-HOLD", CurrentStock = 2, SalePrice = 10 };
        db.AddRange(cashier, other, product);
        await db.SaveChangesAsync();
        var service = new HeldSaleService(db);
        var held = await service.HoldAsync([(product.Id, 1m)], cashier.Id);

        await Assert.ThrowsAsync<PosRuleException>(() => service.CancelAsync(held.Id, other.Id, 1, 10m));
        Assert.Single(await service.SearchAsync(cashier.Id));
        await service.CancelAsync(held.Id, cashier.Id, 1, 10m);
        Assert.Empty(await service.SearchAsync(cashier.Id));
        await service.CancelAsync(null, cashier.Id, 1, 10m);

        var cancellations = await db.AuditEntries.Where(x => x.Action == "Cancel").OrderBy(x => x.Id).ToListAsync();
        Assert.Collection(cancellations,
            heldCancellation => { Assert.Equal("HeldSale", heldCancellation.EntityName); Assert.Equal(held.Id.ToString(), heldCancellation.EntityId); },
            draftCancellation => { Assert.Equal("SaleDraft", draftCancellation.EntityName); Assert.Contains("10.00", draftCancellation.Description); });
    }

    [Fact]
    public async Task HeldSaleCanOnlyBeResumedByOwnerAndIsRemovedAtomicallyOnCompletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Administrator" };
        var otherCashier = new AppUser { UserName = "other", DisplayName = "Other cashier", PasswordHash = "hash", Role = "Cashier" };
        var product = new Product { Name = "Coffee", Sku = "CF-1", CurrentStock = 4, PurchasePrice = 50, SalePrice = 100 };
        db.AddRange(cashier, otherCashier, product);
        await db.SaveChangesAsync();
        var heldSales = new HeldSaleService(db);
        var held = await heldSales.HoldAsync([(product.Id, 2m)], cashier.Id, notes: "Customer returning", billDiscount: 5m,
            discountRates: new Dictionary<int, decimal> { [product.Id] = 10m });

        Assert.Single(await heldSales.SearchAsync(cashier.Id));
        Assert.Empty(await heldSales.SearchAsync(otherCashier.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => heldSales.ResumeAsync(held.Id, otherCashier.Id));
        var resumed = await heldSales.ResumeAsync(held.Id, cashier.Id);
        Assert.Equal("Customer returning", resumed.Sale.Notes);
        Assert.Equal(5m, resumed.Sale.BillDiscount);
        Assert.Equal(10m, Assert.Single(resumed.Items).DiscountRate);
        var item = Assert.Single(resumed.Items);
        Assert.Equal("Coffee", item.ProductName);
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(4m, product.CurrentStock);

        product.CurrentStock = 1m;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PosRuleException>(() => new SaleService(db).CompleteAsync([(item.ProductId, item.Quantity)], cashier.Id, null, 0, 200, "Cash", null, heldSaleId: held.Id));
        Assert.Single(await heldSales.SearchAsync(cashier.Id));
        product.CurrentStock = 4m;
        await db.SaveChangesAsync();

        var sale = await new SaleService(db).CompleteAsync([(item.ProductId, item.Quantity)], cashier.Id, null, resumed.Sale.BillDiscount, 175, "Cash", resumed.Sale.Notes,
            heldSaleId: held.Id, lineDiscountRates: new Dictionary<int, decimal> { [product.Id] = 10m });
        Assert.Equal(2m, product.CurrentStock);
        Assert.Empty(await heldSales.SearchAsync(cashier.Id));
        Assert.Equal("Completed", sale.Status);
        Assert.Equal("Customer returning", sale.Notes);
        Assert.Equal(25m, sale.Discount);
        Assert.Equal(175m, sale.GrandTotal);
        Assert.Contains("Complete", await db.AuditEntries.Where(x => x.EntityName == "HeldSale").Select(x => x.Action).ToListAsync());
    }
}
