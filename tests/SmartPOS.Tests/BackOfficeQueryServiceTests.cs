using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class BackOfficeQueryServiceTests
{
    [Fact]
    public async Task QueriesRequirePermissionsAndReturnOnlyRecentOperationalRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var manager = new AppUser { UserName = "bo-manager", DisplayName = "Manager", PasswordHash = "hash", Role = "Manager" };
        var observer = new AppUser { UserName = "bo-observer", DisplayName = "Observer", PasswordHash = "hash", Role = "Observer" };
        var customer = new Customer { Name = "Credit customer" };
        db.AddRange(manager, observer, customer);
        await db.SaveChangesAsync();
        db.Expenses.Add(new Expense { Description = "Rent", Category = "Occupancy", Amount = 500, UserId = manager.Id, SpentAtUtc = DateTime.UtcNow });
        db.Sales.AddRange(
            new Sale { InvoiceNumber = "INV-OPEN", UserId = manager.Id, CustomerId = customer.Id, RemainingAmount = 125, Status = "Credit", SoldAtUtc = DateTime.UtcNow },
            new Sale { InvoiceNumber = "INV-VOID", UserId = manager.Id, RemainingAmount = 100, Status = "Voided", SoldAtUtc = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();

        var service = new BackOfficeQueryService(db);
        var expenses = await service.GetRecentExpensesAsync(manager.Id);
        var credits = await service.GetOpenCreditSalesAsync(manager.Id);

        Assert.Equal("Rent", Assert.Single(expenses).Description);
        var credit = Assert.Single(credits);
        Assert.Equal("INV-OPEN", credit.InvoiceNumber);
        Assert.Equal("Credit customer", credit.CustomerName);
        Assert.Equal(125m, credit.Balance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetRecentExpensesAsync(observer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOpenCreditSalesAsync(observer.Id));
    }
}
