using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DashboardTopProductsTests
{
    [Fact]
    public async Task GetTopProductsToday_ScopesCashiersSubtractsReturnsAndOmitsVoidedSales()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "dashboard-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var otherCashier = new AppUser { UserName = "dashboard-other", DisplayName = "Other", PasswordHash = "hash", Role = "Cashier" };
        var manager = new AppUser { UserName = "dashboard-manager", DisplayName = "Manager", PasswordHash = "hash", Role = "Manager" };
        var stockManager = new AppUser { UserName = "dashboard-stock", DisplayName = "Stock manager", PasswordHash = "hash", Role = "Inventory Manager" };
        var coffee = new Product { Name = "Coffee", Sku = "COF", SalePrice = 10, PurchasePrice = 5 };
        var tea = new Product { Name = "Tea", Sku = "TEA", SalePrice = 8, PurchasePrice = 4 };
        db.AddRange(cashier, otherCashier, manager, stockManager, coffee, tea);
        await db.SaveChangesAsync();

        var localStart = DateOnly.FromDateTime(DateTime.Today).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nowUtc = TimeZoneInfo.ConvertTimeToUtc(localStart.AddHours(12), TimeZoneInfo.Local);
        var ownSale = new Sale { InvoiceNumber = "DASH-1", UserId = cashier.Id, SoldAtUtc = nowUtc, GrandTotal = 40, Status = "Completed" };
        var otherSale = new Sale { InvoiceNumber = "DASH-2", UserId = otherCashier.Id, SoldAtUtc = nowUtc, GrandTotal = 24, Status = "Completed" };
        var voidedSale = new Sale { InvoiceNumber = "DASH-3", UserId = cashier.Id, SoldAtUtc = nowUtc, GrandTotal = 100, Status = "Voided" };
        db.Sales.AddRange(ownSale, otherSale, voidedSale);
        await db.SaveChangesAsync();
        var coffeeLine = new SaleItem { SaleId = ownSale.Id, ProductId = coffee.Id, ProductName = coffee.Name, Quantity = 4, UnitPrice = 10, UnitCost = 5, Total = 40 };
        var teaLine = new SaleItem { SaleId = otherSale.Id, ProductId = tea.Id, ProductName = tea.Name, Quantity = 4, UnitPrice = 8, UnitCost = 4, Total = 32 };
        var voidedLine = new SaleItem { SaleId = voidedSale.Id, ProductId = tea.Id, ProductName = tea.Name, Quantity = 20, UnitPrice = 8, UnitCost = 4, Total = 160 };
        db.SaleItems.AddRange(coffeeLine, teaLine, voidedLine);
        await db.SaveChangesAsync();
        var returned = new SaleReturn { ReturnNumber = "RET-DASH-1", SaleId = ownSale.Id, UserId = manager.Id, ReturnedAtUtc = nowUtc, Total = 10, Reason = "Returned" };
        db.Returns.Add(returned);
        await db.SaveChangesAsync();
        db.ReturnItems.Add(new SaleReturnItem { SaleReturnId = returned.Id, SaleItemId = coffeeLine.Id, ProductId = coffee.Id, Quantity = 1, Refund = 10 });
        await db.SaveChangesAsync();

        var dashboard = new DashboardService(db);
        var cashierRows = await dashboard.GetTopProductsTodayAsync(cashier.Id);
        var managerRows = await dashboard.GetTopProductsTodayAsync(manager.Id);
        var stockManagerRows = await dashboard.GetTopProductsTodayAsync(stockManager.Id);

        Assert.Equal("Coffee", Assert.Single(cashierRows).ProductName);
        Assert.Equal(3m, cashierRows[0].UnitsSold);
        Assert.Equal(30m, cashierRows[0].Revenue);
        Assert.Equal(new[] { "Tea", "Coffee" }, managerRows.Select(x => x.ProductName));
        Assert.Equal(4m, managerRows.Single(x => x.ProductId == tea.Id).UnitsSold);
        Assert.Equal(managerRows.Select(x => x.ProductId), stockManagerRows.Select(x => x.ProductId));
    }
}
