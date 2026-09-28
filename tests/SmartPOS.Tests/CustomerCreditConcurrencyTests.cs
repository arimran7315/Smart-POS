using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class CustomerCreditConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCreditSalesCannotExceedCustomerCreditLimit()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"credit-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connectionString).Options;
        int userId;
        int customerId;
        int[] productIds;
        await using (var setup = new PosDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var admin = new AppUser { UserName = "credit-race-admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
            var customer = new Customer { Name = "Credit race customer", CreditLimit = 10m };
            setup.AddRange(admin, customer);
            for (var i = 0; i < 6; i++)
                setup.Products.Add(new Product { Name = $"Credit item {i}", Sku = $"CREDIT-RACE-{i}", CurrentStock = 1, PurchasePrice = 1, SalePrice = 10 });
            await setup.SaveChangesAsync();
            userId = admin.Id;
            customerId = customer.Id;
            productIds = await setup.Products.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        }

        var attempts = await Task.WhenAll(productIds.Select(async productId =>
        {
            await using var db = new PosDbContext(options);
            try
            {
                var sale = await new SaleService(db).CompleteAsync([(productId, 1m)], userId, customerId, 0, 0, "Credit", null);
                return (Sale: sale, Error: (Exception?)null);
            }
            catch (Exception error) { return (Sale: (Sale?)null, Error: error); }
        }));

        await using var verify = new PosDbContext(options);
        var persistedSales = await verify.Sales.AsNoTracking().Where(x => x.CustomerId == customerId).ToListAsync();
        var accepted = Assert.Single(persistedSales);
        Assert.Equal(10m, accepted.RemainingAmount);
        Assert.Equal(10m, persistedSales.Sum(x => x.RemainingAmount));
        Assert.Single(attempts, x => x.Sale is not null);
        Assert.Equal(productIds.Length - 1, attempts.Count(x => x.Error is not null));
        Assert.Equal(1, await verify.StockMovements.CountAsync(x => x.Reason == "Sale"));
    }
}
