using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SalesByProductReportTests
{
    [Fact]
    public async Task FullProductReportNetsSaleAndReturnActivityWithinTheSelectedRange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var cashier = new AppUser { UserName = "product-report-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Administrator" };
        var milk = new Product { Name = "Milk", Sku = "PRODUCT-REPORT-MILK" };
        var bread = new Product { Name = "Bread", Sku = "PRODUCT-REPORT-BREAD" };
        var cocoa = new Product { Name = "Cocoa", Sku = "PRODUCT-REPORT-COCOA" };
        db.AddRange(cashier, milk, bread, cocoa);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var soldToday = new Sale
        {
            InvoiceNumber = "PRODUCT-REPORT-TODAY", UserId = cashier.Id, SoldAtUtc = LocalNoon(today),
            Items =
            [
                new SaleItem { ProductId = milk.Id, ProductName = milk.Name, Quantity = 2, UnitPrice = 100, Total = 200 },
                new SaleItem { ProductId = bread.Id, ProductName = bread.Name, Quantity = 1, UnitPrice = 30, Total = 30 }
            ]
        };
        var soldEarlier = new Sale
        {
            InvoiceNumber = "PRODUCT-REPORT-EARLIER", UserId = cashier.Id, SoldAtUtc = LocalNoon(today.AddDays(-5)),
            Items = [new SaleItem { ProductId = cocoa.Id, ProductName = cocoa.Name, Quantity = 3, UnitPrice = 30, Total = 90 }]
        };
        db.AddRange(soldToday, soldEarlier);
        await db.SaveChangesAsync();

        db.Returns.AddRange(
            new SaleReturn
            {
                ReturnNumber = "PRODUCT-REPORT-RETURN-MILK", SaleId = soldToday.Id, UserId = cashier.Id,
                ReturnedAtUtc = LocalNoon(today), Total = 100, Reason = "Returned milk",
                Items = [new SaleReturnItem { SaleItemId = soldToday.Items[0].Id, ProductId = milk.Id, Quantity = 1, Refund = 100 }]
            },
            new SaleReturn
            {
                ReturnNumber = "PRODUCT-REPORT-RETURN-COCOA", SaleId = soldEarlier.Id, UserId = cashier.Id,
                ReturnedAtUtc = LocalNoon(today), Total = 30, Reason = "Returned cocoa",
                Items = [new SaleReturnItem { SaleItemId = soldEarlier.Items[0].Id, ProductId = cocoa.Id, Quantity = 1, Refund = 30 }]
            });
        milk.Name = "Fresh Milk";
        await db.SaveChangesAsync();

        var report = await new ReportsService(db).GetSalesByProductAsync(cashier.Id, today, today);

        Assert.Collection(report,
            breadRow => { Assert.Equal("Bread", breadRow.ProductName); Assert.Equal(1m, breadRow.UnitsSold); Assert.Equal(30m, breadRow.Revenue); },
            cocoaRow => { Assert.Equal("Cocoa", cocoaRow.ProductName); Assert.Equal(-1m, cocoaRow.UnitsSold); Assert.Equal(-30m, cocoaRow.Revenue); },
            milkRow => { Assert.Equal("Fresh Milk", milkRow.ProductName); Assert.Equal(1m, milkRow.UnitsSold); Assert.Equal(100m, milkRow.Revenue); });

        var topProducts = await new ReportsService(db).GetTopProductsAsync(cashier.Id, today, today);
        Assert.Equal(new[] { "Bread", "Milk" }, topProducts.Select(x => x.ProductName));
    }

    private static DateTime LocalNoon(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local);
    }
}
