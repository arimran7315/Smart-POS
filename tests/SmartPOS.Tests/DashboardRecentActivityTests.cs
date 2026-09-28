using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DashboardRecentActivityTests
{
    [Fact]
    public async Task RecentActivity_EnforcesSalesAndPurchasePermissionsAndScopesCashierSales()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "recent-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var manager = new AppUser { UserName = "recent-manager", DisplayName = "Manager", PasswordHash = "hash", Role = "Manager" };
        var stockManager = new AppUser { UserName = "recent-stock", DisplayName = "Stock", PasswordHash = "hash", Role = "Inventory Manager" };
        var supplier = new Supplier { Name = "Supplier A" };
        db.AddRange(cashier, manager, stockManager, supplier);
        await db.SaveChangesAsync();

        db.Sales.AddRange(
            new Sale { InvoiceNumber = "RECENT-C", UserId = cashier.Id, SoldAtUtc = DateTime.UtcNow.AddMinutes(-2), GrandTotal = 10, Status = "Completed" },
            new Sale { InvoiceNumber = "RECENT-M", UserId = manager.Id, SoldAtUtc = DateTime.UtcNow.AddMinutes(-1), GrandTotal = 20, Status = "Credit" });
        db.Purchases.Add(new Purchase { InvoiceNumber = "RECENT-P", SupplierId = supplier.Id, UserId = manager.Id, PurchasedAtUtc = DateTime.UtcNow, Total = 30, Status = "Received" });
        await db.SaveChangesAsync();

        var dashboard = new DashboardService(db);
        Assert.Equal("RECENT-C", Assert.Single(await dashboard.GetRecentSalesAsync(cashier.Id)).InvoiceNumber);
        Assert.Equal(new[] { "RECENT-M", "RECENT-C" }, (await dashboard.GetRecentSalesAsync(manager.Id)).Select(x => x.InvoiceNumber));
        Assert.Equal("RECENT-P", Assert.Single(await dashboard.GetRecentPurchasesAsync(manager.Id)).InvoiceNumber);
        Assert.Equal("Supplier A", Assert.Single(await dashboard.GetRecentPurchasesAsync(stockManager.Id)).SupplierName);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dashboard.GetRecentPurchasesAsync(cashier.Id));
    }
}
