using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class InventoryConcurrencyTests
{
    [Fact]
    public async Task StaleStockUpdateIsRejectedInsteadOfOverwritingAnotherTillsStockChange()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"stock-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connectionString).Options;
        await using (var setup = new PosDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Products.Add(new Product { Name = "Shared stock", Sku = "CONCURRENCY", CurrentStock = 5 });
            await setup.SaveChangesAsync();
        }

        await using var firstTill = new PosDbContext(options);
        await using var secondTill = new PosDbContext(options);
        var firstProduct = await firstTill.Products.SingleAsync();
        var secondProduct = await secondTill.Products.SingleAsync();
        firstProduct.CurrentStock -= 2;
        firstProduct.StockVersion++;
        secondProduct.CurrentStock -= 2;
        secondProduct.StockVersion++;

        await firstTill.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondTill.SaveChangesAsync());

        await using var verify = new PosDbContext(options);
        var saved = await verify.Products.SingleAsync();
        Assert.Equal(3m, saved.CurrentStock);
        Assert.Equal(1, saved.StockVersion);
    }
}
