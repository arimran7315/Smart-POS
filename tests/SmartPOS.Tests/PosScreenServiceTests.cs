using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PosScreenServiceTests
{
    [Fact]
    public async Task GetSetupAsync_RequiresSalesPermissionAndOnlyReturnsAuthorizedCustomerOptions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var observer = new AppUser { UserName = "pos-observer", DisplayName = "Observer", PasswordHash = "hash", Role = "Observer" };
        var customer = new Customer { Name = "Walk-in Customer", Email = "private@example.invalid", Phone = "private" };
        db.AddRange(cashier, observer, customer);
        db.Settings.Add(new AppSetting { Key = "StoreName", Value = "Corner Shop" });
        await db.SaveChangesAsync();

        var service = new PosScreenService(db);
        var setup = await service.GetSetupAsync(cashier.Id, "SmartPOS");

        Assert.Equal("Corner Shop", setup.StoreName);
        Assert.Equal(2, setup.Customers.Count);
        Assert.Null(setup.Customers[0].CustomerId);
        Assert.Equal("Walk-in", setup.Customers[0].Name);
        Assert.Equal(new PosCustomerOption(customer.Id, customer.Name), setup.Customers[1]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSetupAsync(observer.Id, "SmartPOS"));
    }
}
