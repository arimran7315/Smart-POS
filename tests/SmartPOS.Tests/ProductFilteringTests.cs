using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ProductFilteringTests
{
    [Fact]
    public async Task SearchAsync_FiltersCategoryAndLowStockBeforeSortingAndPaging()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "filter-admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var produce = new Category { Name = "Produce" };
        var tools = new Category { Name = "Tools" };
        db.AddRange(admin, produce, tools);
        db.Products.AddRange(
            new Product { Name = "Apple", Sku = "P-1", Category = produce, CurrentStock = 1, MinimumStock = 5, SalePrice = 5 },
            new Product { Name = "Banana", Sku = "P-2", Category = produce, CurrentStock = 10, MinimumStock = 5, SalePrice = 2 },
            new Product { Name = "Hammer", Sku = "T-1", Category = tools, CurrentStock = 0, MinimumStock = 1, SalePrice = 20 },
            new Product { Name = "Inactive apple", Sku = "P-3", Category = produce, CurrentStock = 0, MinimumStock = 2, SalePrice = 1, IsActive = false });
        await db.SaveChangesAsync();

        var service = new ProductService(db);
        var produceLowStock = await service.SearchAsync("", admin.Id, categoryFilter: "rod", lowStockOnly: true);
        var byPrice = await service.SearchAsync("", admin.Id, sortBy: "Sale price");
        var byStock = await service.SearchAsync("", admin.Id, sortBy: "Stock", skip: 1, take: 1);

        Assert.Equal("Apple", Assert.Single(produceLowStock).Name);
        Assert.Equal(new[] { "Banana", "Apple", "Hammer" }, byPrice.Select(x => x.Name));
        Assert.Equal("Apple", Assert.Single(byStock).Name);
    }
}
