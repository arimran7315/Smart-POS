using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ProductPaginationTests
{
    [Fact]
    public async Task SearchAsync_ReturnsOrderedPagesWithoutRepeatsOrSkippedRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "catalog-admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        db.Users.Add(admin);
        db.Products.AddRange(Enumerable.Range(0, 105).Select(index => new Product
        {
            Name = $"Product {index:D3}", Sku = $"SKU-{index:D3}", SalePrice = 1
        }));
        await db.SaveChangesAsync();

        var service = new ProductService(db);
        var firstPage = await service.SearchAsync("", admin.Id, skip: 0, take: 50);
        var secondPage = await service.SearchAsync("", admin.Id, skip: 50, take: 50);
        var lastPage = await service.SearchAsync("", admin.Id, skip: 100, take: 50);

        Assert.Equal(50, firstPage.Count);
        Assert.Equal(50, secondPage.Count);
        Assert.Equal(5, lastPage.Count);
        Assert.Equal("Product 000", firstPage[0].Name);
        Assert.Equal("Product 049", firstPage[^1].Name);
        Assert.Equal("Product 050", secondPage[0].Name);
        Assert.Equal("Product 099", secondPage[^1].Name);
        Assert.Equal("Product 100", lastPage[0].Name);
        Assert.Equal(105, firstPage.Concat(secondPage).Concat(lastPage).Select(x => x.Id).Distinct().Count());
    }
}
