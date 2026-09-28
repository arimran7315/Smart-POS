using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class UserManagementSecurityTests
{
    [Fact]
    public async Task RoleNamesAreCanonicalAndAdministratorSafetyChecksIgnoreCasing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Administrator", PasswordHash = "hash", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var service = new UserManagementService(db, new PasswordHasher());

        var secondary = await service.CreateAsync("secondary", "Secondary", "a sufficiently long password", "administrator", admin.Id);
        Assert.Equal("Administrator", secondary.Role);
        secondary.Role = "administrator"; // Existing account data may contain mixed casing.
        await db.SaveChangesAsync();

        var updated = await service.UpdateAsync(secondary.Id, "Secondary Admin", "manager", null, admin.Id);
        Assert.Equal("Manager", updated.Role);
        Assert.Equal("Administrator", admin.Role);
        var cashier = await service.CreateAsync("cashier", "Cashier", "another sufficiently long password", "cAsHiEr", admin.Id);
        Assert.Equal("Cashier", cashier.Role);
    }

    [Fact]
    public async Task SearchAsync_RequiresUserManagementAndNeverReturnsPasswordHashes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Administrator", PasswordHash = "sensitive-hash", Role = "Administrator" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "another-hash", Role = "Cashier" };
        db.AddRange(admin, cashier);
        await db.SaveChangesAsync();

        var service = new UserManagementService(db, new PasswordHasher());
        var accounts = await service.SearchAsync(admin.Id);

        Assert.Equal(2, accounts.Count);
        Assert.DoesNotContain(accounts, account => account.GetType().GetProperty("PasswordHash") is not null);
        Assert.Equal("Cashier", Assert.Single(accounts, account => account.UserName == "cashier").Role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchAsync(cashier.Id));
    }

    [Fact]
    public async Task ConcurrentAdministratorDeactivation_AlwaysLeavesAnActiveAdministrator()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SmartPOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "users.db");
        try
        {
            var connectionString = $"Data Source={databasePath};Default Timeout=10";
            var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connectionString).Options;
            int firstId;
            int secondId;
            await using (var setup = new PosDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                var first = new AppUser { UserName = "first-admin", DisplayName = "First", PasswordHash = "hash", Role = "Administrator" };
                var second = new AppUser { UserName = "second-admin", DisplayName = "Second", PasswordHash = "hash", Role = "Administrator" };
                setup.AddRange(first, second);
                await setup.SaveChangesAsync();
                firstId = first.Id;
                secondId = second.Id;
            }

            async Task<bool> DeactivateAsync(int targetId, int actorId)
            {
                try
                {
                    await using var context = new PosDbContext(options);
                    await new UserManagementService(context, new PasswordHasher()).DeactivateAsync(targetId, actorId);
                    return true;
                }
                catch (UnauthorizedAccessException) { return false; }
                catch (PosRuleException) { return false; }
            }

            var outcomes = await Task.WhenAll(
                Task.Run(() => DeactivateAsync(secondId, firstId)),
                Task.Run(() => DeactivateAsync(firstId, secondId)));

            await using var verify = new PosDbContext(options);
            Assert.Contains(true, outcomes);
            Assert.True(await verify.Users.CountAsync(x => x.IsActive && x.Role == "Administrator") >= 1);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
