using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ShiftWorkflowTests
{
    [Fact]
    public async Task OpenSummarizeAndClose_ReconcilesCashDrawer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        var product = new Product { Name = "Soda", Sku = "S-1", CurrentStock = 100, PurchasePrice = 20, SalePrice = 100, TaxRate = 0 };
        db.AddRange(cashier, product);
        await db.SaveChangesAsync();

        var shifts = new ShiftService(db);
        var shift = await shifts.OpenAsync(cashier.Id, 50m);
        Assert.Equal("Open", shift.Status);
        await Assert.ThrowsAsync<PosRuleException>(() => shifts.OpenAsync(cashier.Id, 10m));
        Assert.NotNull(await shifts.GetOpenShiftAsync(cashier.Id));

        await new SaleService(db).CompleteAsync([(product.Id, 2)], cashier.Id, null, 0m, 200m, "Cash", null);

        var summary = await shifts.GetSummaryAsync(shift.Id, cashier.Id);
        Assert.Equal(1, summary.SaleCount);
        Assert.Equal(200m, summary.CashSales);
        Assert.Equal(250m, summary.ExpectedCash);

        var closed = await shifts.CloseAsync(shift.Id, 260m, "ten over", cashier.Id);
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(250m, closed.ExpectedCash);
        Assert.Equal(10m, closed.CashVariance);
        await Assert.ThrowsAsync<PosRuleException>(() => shifts.CloseAsync(shift.Id, 100m, null, cashier.Id));
    }

    [Fact]
    public async Task ShiftManagementRequiresPermission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var inventoryManager = new AppUser { UserName = "inv", DisplayName = "Inv", PasswordHash = "test", Role = "Inventory Manager" };
        db.Users.Add(inventoryManager);
        await db.SaveChangesAsync();
        var shifts = new ShiftService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shifts.OpenAsync(inventoryManager.Id, 0m));
    }

    [Fact]
    public async Task ManagerCanViewAnotherCashiersShift_CashierCannot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        var otherCashier = new AppUser { UserName = "cashier2", DisplayName = "Cashier 2", PasswordHash = "test", Role = "Cashier" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test", Role = "Manager" };
        db.AddRange(cashier, otherCashier, manager);
        await db.SaveChangesAsync();
        var shifts = new ShiftService(db);
        var shift = await shifts.OpenAsync(cashier.Id, 25m);

        var managerView = await shifts.GetSummaryAsync(shift.Id, manager.Id);
        Assert.Equal(25m, managerView.OpeningFloat);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => shifts.GetSummaryAsync(shift.Id, otherCashier.Id));
    }
}
