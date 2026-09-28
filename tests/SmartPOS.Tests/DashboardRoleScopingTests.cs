using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DashboardRoleScopingTests
{
    [Fact]
    public async Task DashboardCardsAndTrends_RespectSalesPurchaseAndExpensePermissions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "scope-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var stock = new AppUser { UserName = "scope-stock", DisplayName = "Stock", PasswordHash = "hash", Role = "Inventory Manager" };
        var manager = new AppUser { UserName = "scope-manager", DisplayName = "Manager", PasswordHash = "hash", Role = "Manager" };
        var supplier = new Supplier { Name = "Dashboard supplier" };
        var localNoon = DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Unspecified);
        var todayUtc = TimeZoneInfo.ConvertTimeToUtc(localNoon, TimeZoneInfo.Local);
        db.AddRange(cashier, stock, manager, supplier);
        await db.SaveChangesAsync();
        db.Sales.AddRange(
            new Sale { InvoiceNumber = "SCOPE-C", UserId = cashier.Id, SoldAtUtc = todayUtc, GrandTotal = 100, Status = "Completed" },
            new Sale { InvoiceNumber = "SCOPE-M", UserId = manager.Id, SoldAtUtc = todayUtc, GrandTotal = 200, Status = "Completed" });
        db.Purchases.Add(new Purchase { InvoiceNumber = "SCOPE-P", SupplierId = supplier.Id, UserId = manager.Id, PurchasedAtUtc = todayUtc, Total = 300, Status = "Received" });
        db.Expenses.Add(new Expense { Description = "Supplies", Category = "Operations", Amount = 50, UserId = manager.Id, SpentAtUtc = todayUtc });
        await db.SaveChangesAsync();

        var dashboard = new DashboardService(db);
        var cashierSnapshot = await dashboard.GetTodayAsync(cashier.Id);
        var stockSnapshot = await dashboard.GetTodayAsync(stock.Id);
        var managerSnapshot = await dashboard.GetTodayAsync(manager.Id);
        Assert.Equal(100m, cashierSnapshot.TodaySales);
        Assert.Null(cashierSnapshot.TodayPurchases);
        Assert.Null(cashierSnapshot.TodayExpenses);
        Assert.Null(cashierSnapshot.TodayProfit);
        Assert.Null(stockSnapshot.TodaySales);
        Assert.Equal(300m, stockSnapshot.TodayPurchases);
        Assert.Null(stockSnapshot.TodayExpenses);
        Assert.Null(stockSnapshot.TodayProfit);
        Assert.Equal(300m, managerSnapshot.TodaySales);
        Assert.Equal(300m, managerSnapshot.TodayPurchases);
        Assert.Equal(50m, managerSnapshot.TodayExpenses);

        var cashierTrend = Assert.Single(await dashboard.GetLastSevenDaysAsync(cashier.Id), point => point.Day == DateOnly.FromDateTime(DateTime.Today));
        var stockTrend = Assert.Single(await dashboard.GetLastSevenDaysAsync(stock.Id), point => point.Day == DateOnly.FromDateTime(DateTime.Today));
        var managerTrend = Assert.Single(await dashboard.GetLastSevenDaysAsync(manager.Id), point => point.Day == DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(100m, cashierTrend.Sales);
        Assert.Equal(0m, cashierTrend.Purchases);
        Assert.Equal(0m, stockTrend.Sales);
        Assert.Equal(300m, stockTrend.Purchases);
        Assert.Equal(300m, managerTrend.Sales);
        Assert.Equal(300m, managerTrend.Purchases);
    }
}
