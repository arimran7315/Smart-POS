using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleProductLookupSecurityTests
{
    [Fact]
    public async Task SearchForSale_RequiresActiveSalesUserAndReturnsOnlySaleFacingFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "lookup-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var inventoryManager = new AppUser { UserName = "lookup-stock", DisplayName = "Stock", PasswordHash = "hash", Role = "Inventory Manager" };
        var inactiveCashier = new AppUser { UserName = "lookup-disabled", DisplayName = "Disabled", PasswordHash = "hash", Role = "Cashier", IsActive = false };
        var product = new Product { Name = "Coffee", Sku = "COF-1", Barcode = "123", PurchasePrice = 42, SalePrice = 80, CurrentStock = 3, TaxRate = 5 };
        db.AddRange(cashier, inventoryManager, inactiveCashier, product);
        await db.SaveChangesAsync();

        var service = new ProductService(db);
        var found = Assert.Single(await service.SearchForSaleAsync("123", cashier.Id));
        Assert.Equal("Coffee", found.ProductName);
        Assert.Equal(80m, found.SalePrice);
        Assert.Equal(3m, found.AvailableStock);
        Assert.Null(found.GetType().GetProperty("PurchasePrice"));
        Assert.Empty(await service.SearchForSaleAsync("missing", cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchForSaleAsync("123", inventoryManager.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchForSaleAsync("123", inactiveCashier.Id));
    }
}
