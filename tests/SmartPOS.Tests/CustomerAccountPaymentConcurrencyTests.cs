using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class CustomerAccountPaymentConcurrencyTests
{
    [Fact]
    public async Task ConcurrentAccountReceiptsCannotExceedOpeningDebt()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"account-payment-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connectionString).Options;
        int adminId;
        int customerId;
        await using (var setup = new PosDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var admin = new AppUser { UserName = "account-payment-admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
            var customer = new Customer { Name = "Concurrent account customer", OpeningBalance = 100m };
            setup.AddRange(admin, customer);
            await setup.SaveChangesAsync();
            adminId = admin.Id;
            customerId = customer.Id;
        }

        var attempts = await Task.WhenAll(Enumerable.Range(1, 6).Select(async attempt =>
        {
            await using var db = new PosDbContext(options);
            try
            {
                await new CustomerAccountPaymentService(db).ReceiveAsync(customerId, 20m, adminId, "Cash", $"RACE-{attempt}");
                return (Accepted: true, Error: (Exception?)null);
            }
            catch (Exception error) { return (Accepted: false, Error: error); }
        }));

        await using var verify = new PosDbContext(options);
        var payments = await verify.CustomerAccountPayments.AsNoTracking().Where(x => x.CustomerId == customerId).ToListAsync();
        Assert.Equal(5, payments.Count);
        Assert.Equal(100m, payments.Sum(x => x.Amount));
        Assert.Equal(5, attempts.Count(x => x.Accepted));
        Assert.Equal(1, attempts.Count(x => x.Error is PosRuleException));
        Assert.Equal(5, await verify.AuditEntries.CountAsync(x => x.EntityName == "Customer" && x.EntityId == customerId.ToString() && x.Action == "Payment"));
    }
}
