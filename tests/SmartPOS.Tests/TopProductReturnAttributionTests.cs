using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class TopProductReturnAttributionTests
{
    [Fact]
    public async Task TopProducts_DoesNotSubtractReturnForSaleOutsideSelectedPeriod()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var product = new Product { Name = "Current product", Sku = "CURRENT", CurrentStock = 10, SalePrice = 100, PurchasePrice = 50 };
        db.AddRange(admin, product);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var soldToday = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(12))), TimeZoneInfo.Local);
        var oldSale = new Sale
        {
            InvoiceNumber = "OLD-RETURN-SALE", UserId = admin.Id, SoldAtUtc = soldToday.AddDays(-10),
            Subtotal = 500, GrandTotal = 500, PaidAmount = 500,
            Items = [new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 5, UnitPrice = 100, UnitCost = 50, Total = 500 }]
        };
        var currentSale = new Sale
        {
            InvoiceNumber = "CURRENT-SALE", UserId = admin.Id, SoldAtUtc = soldToday,
            Subtotal = 100, GrandTotal = 100, PaidAmount = 100,
            Items = [new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPrice = 100, UnitCost = 50, Total = 100 }]
        };
        db.Sales.AddRange(oldSale, currentSale);
        await db.SaveChangesAsync();
        db.Returns.Add(new SaleReturn
        {
            ReturnNumber = "RETURN-OLD-SALE", SaleId = oldSale.Id, UserId = admin.Id, ReturnedAtUtc = soldToday,
            Total = 400, Reason = "Return of older sale",
            Items = [new SaleReturnItem { SaleItemId = oldSale.Items[0].Id, ProductId = product.Id, Quantity = 4, Refund = 400 }]
        });
        await db.SaveChangesAsync();

        var result = await new ReportsService(db).GetTopProductsAsync(admin.Id, today, today);

        var row = Assert.Single(result);
        Assert.Equal(1m, row.UnitsSold);
        Assert.Equal(100m, row.Revenue);
    }
}
