using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class AuthenticationLockoutTests
{
    [Fact]
    public async Task ConcurrentBadPasswordsAreSerializedAndAllCountTowardLockout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SmartPOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "concurrent-login.db");
        try
        {
            var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite($"Data Source={databasePath};Default Timeout=30").Options;
            int userId;
            var hasher = new PasswordHasher();
            await using (var setup = new PosDbContext(options))
            {
                await setup.Database.MigrateAsync();
                var user = new AppUser { UserName = "parallel-operator", DisplayName = "Operator", PasswordHash = hasher.Hash("correct horse battery staple"), Role = "Cashier" };
                setup.Users.Add(user);
                await setup.SaveChangesAsync();
                userId = user.Id;
            }

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var db = new PosDbContext(options);
                return await new AuthenticationService(db, hasher).LoginAsync("parallel-operator", "incorrect password");
            }));

            Assert.All(outcomes, Assert.Null);
            await using var verify = new PosDbContext(options);
            var locked = await verify.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
            Assert.Equal(5, locked.FailedLoginAttempts);
            Assert.True(locked.LockoutUntilUtc > DateTime.UtcNow);
            Assert.Equal(5, await verify.AuditEntries.CountAsync(x => x.UserId == userId && x.Action == "FailedSignIn"));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task FiveBadPasswordsLockAccountAndSuccessfulLoginResetsExpiredLockout()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var hasher = new PasswordHasher();
        var user = new AppUser { UserName = "operator", DisplayName = "Operator", PasswordHash = hasher.Hash("correct horse battery staple"), Role = "Cashier" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var authentication = new AuthenticationService(db, hasher);

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Null(await authentication.LoginAsync("operator", "incorrect password"));
        db.ChangeTracker.Clear();
        var locked = await db.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(5, locked.FailedLoginAttempts);
        Assert.True(locked.LockoutUntilUtc > DateTime.UtcNow);
        Assert.Null(await authentication.LoginAsync("operator", "correct horse battery staple"));
        Assert.Equal(5, await db.AuditEntries.CountAsync(x => x.Action == "FailedSignIn"));
        Assert.Empty(await db.AuditEntries.Where(x => x.Action == "SignIn").ToListAsync());

        locked.LockoutUntilUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.NotNull(await authentication.LoginAsync("operator", "correct horse battery staple"));
        db.ChangeTracker.Clear();
        var signedIn = await db.Users.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(0, signedIn.FailedLoginAttempts);
        Assert.Null(signedIn.LockoutUntilUtc);
        Assert.Single(await db.AuditEntries.Where(x => x.Action == "SignIn").ToListAsync());
    }
}
