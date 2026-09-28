using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleHistoryFilterTests
{
    [Fact]
    public async Task SearchAsync_AppliesInclusiveLocalDatesStatusAndExistingRoleVisibility()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "history-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var manager = new AppUser { UserName = "history-manager", DisplayName = "Manager", PasswordHash = "hash", Role = "Manager" };
        db.AddRange(cashier, manager);
        await db.SaveChangesAsync();

        var day = DateOnly.FromDateTime(DateTime.Today);
        DateTime LocalUtc(DateOnly date, int hour) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified), TimeZoneInfo.Local);
        var ownCompleted = new Sale { InvoiceNumber = "INV-H-1", UserId = cashier.Id, SoldAtUtc = LocalUtc(day, 0), Status = "Completed" };
        var ownVoided = new Sale { InvoiceNumber = "INV-H-2", UserId = cashier.Id, SoldAtUtc = LocalUtc(day, 23), Status = "Voided" };
        var ownPrior = new Sale { InvoiceNumber = "INV-H-3", UserId = cashier.Id, SoldAtUtc = LocalUtc(day.AddDays(-1), 12), Status = "Completed" };
        var managerSale = new Sale { InvoiceNumber = "INV-H-4", UserId = manager.Id, SoldAtUtc = LocalUtc(day, 12), Status = "Completed" };
        db.Sales.AddRange(ownCompleted, ownVoided, ownPrior, managerSale);
        await db.SaveChangesAsync();

        var service = new SaleService(db);
        var cashierDay = await service.SearchAsync(cashier.Id, "", from: day, through: day);
        var cashierVoided = await service.SearchAsync(cashier.Id, "", from: day, through: day, status: "Voided");
        var managerCompleted = await service.SearchAsync(manager.Id, "", from: day, through: day, status: "Completed");
        var firstPage = await service.SearchPageAsync(cashier.Id, "", pageIndex: 0, pageSize: 1, from: day, through: day);
        var secondPage = await service.SearchPageAsync(cashier.Id, "", pageIndex: 1, pageSize: 1, from: day, through: day);

        Assert.Equal(new[] { ownVoided.Id, ownCompleted.Id }, cashierDay.Select(x => x.Id));
        Assert.Equal(ownVoided.Id, Assert.Single(cashierVoided).Id);
        Assert.Equal(2, managerCompleted.Count);
        Assert.Contains(managerCompleted, x => x.Id == managerSale.Id);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.PageCount);
        Assert.Equal(ownVoided.Id, Assert.Single(firstPage.Items).Id);
        Assert.Equal(ownCompleted.Id, Assert.Single(secondPage.Items).Id);
        Assert.Equal(1, secondPage.PageIndex);
        await Assert.ThrowsAsync<PosRuleException>(() => service.SearchAsync(cashier.Id, "", from: day, through: day.AddDays(-1)));
        await Assert.ThrowsAsync<PosRuleException>(() => service.SearchAsync(cashier.Id, "", status: "Not a status"));
    }
}
