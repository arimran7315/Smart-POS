using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleItemDiscountTests
{
    [Fact]
    public async Task ItemDiscountOverrideIsPermissionCheckedAndPersistsToSaleLine()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        var product = new Product { Name = "Discounted item", Sku = "ITEM-DISC", CurrentStock = 3, PurchasePrice = 50, SalePrice = 100, TaxRate = 20 };
        db.AddRange(admin, cashier, product);
        await db.SaveChangesAsync();

        var service = new SaleService(db);
        var overrideRate = new Dictionary<int, decimal> { [product.Id] = 15m };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CompleteAsync([(product.Id, 1m)], cashier.Id,
            null, 0, 102m, "Cash", null, lineDiscountRates: overrideRate));
        Assert.Equal(3m, product.CurrentStock);

        var sale = await service.CompleteAsync([(product.Id, 1m)], admin.Id, null, 0, 102m, "Cash", null, lineDiscountRates: overrideRate);
        var item = Assert.Single(sale.Items);
        Assert.Equal(15m, item.Discount);
        Assert.Equal(17m, item.Tax);
        Assert.Equal(102m, item.Total);
        Assert.Equal(2m, product.CurrentStock);
    }
}
