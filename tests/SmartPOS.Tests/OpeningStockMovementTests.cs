using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class OpeningStockMovementTests
{
    [Fact]
    public async Task ProductCreationRecordsOpeningStockAndProductEditsCannotChangeStock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();

        var service = new ProductService(db);
        var saved = await service.SaveAsync(new Product
        {
            Name = "Opening stock item", Sku = "OPEN-1", CurrentStock = 12,
            PurchasePrice = 4, SalePrice = 8
        }, admin.Id);
        var movement = Assert.Single(await db.StockMovements.ToListAsync());
        Assert.Equal(saved.Id, movement.ProductId);
        Assert.Equal(12m, movement.QuantityDelta);
        Assert.Equal(12m, movement.BalanceAfter);
        Assert.Equal("OpeningStock", movement.Reason);

        await service.SaveAsync(new Product
        {
            Id = saved.Id, Name = "Renamed item", Sku = saved.Sku,
            CurrentStock = 0, PurchasePrice = 5, SalePrice = 9,
            CreatedAtUtc = saved.CreatedAtUtc
        }, admin.Id);

        Assert.Equal(12m, (await db.Products.SingleAsync(x => x.Id == saved.Id)).CurrentStock);
        Assert.Single(await db.StockMovements.ToListAsync());
    }
}
