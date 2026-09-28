using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleVisibilityTests
{
    [Fact]
    public async Task CashierSeesOwnSalesWhileManagerCanSeeAllSales()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test-hash", Role = "Cashier" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test-hash", Role = "Manager" };
        var product = new Product { Name = "Item", Sku = "ITEM", CurrentStock = 10, SalePrice = 100, PurchasePrice = 50 };
        db.AddRange(cashier, manager, product);
        await db.SaveChangesAsync();
        var service = new SaleService(db);
        var cashierSale = await service.CompleteAsync([(product.Id, 1m)], cashier.Id, null, 0, 100, "Cash", null);
        var managerSale = await service.CompleteAsync([(product.Id, 1m)], manager.Id, null, 0, 100, "Cash", null);

        var cashierResults = await service.SearchAsync(cashier.Id, "");
        var managerResults = await service.SearchAsync(manager.Id, "");

        Assert.Equal([cashierSale.Id], cashierResults.Select(x => x.Id));
        Assert.Equal(2, managerResults.Count);
        Assert.Contains(managerResults, x => x.Id == managerSale.Id);
        Assert.Contains(managerResults, x => x.Id == cashierSale.Id);
    }
}
