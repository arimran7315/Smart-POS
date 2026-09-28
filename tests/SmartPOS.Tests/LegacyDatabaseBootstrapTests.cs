using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class LegacyDatabaseBootstrapTests
{
    private const string InitialMigration = "20260927104303_InitialSqlite";

    [Fact]
    public async Task PrepareForMigrations_BaselinesMatchingLegacySchemaThenAppliesLaterMigrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync(InitialMigration);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (UserName, DisplayName, PasswordHash, Role, IsActive, CreatedAtUtc) VALUES ('legacy-admin', 'Legacy admin', 'hash', 'Administrator', 1, '2026-01-01T00:00:00Z')");
        await using var userIdCommand = db.Database.GetDbConnection().CreateCommand();
        userIdCommand.CommandText = "SELECT Id FROM Users WHERE UserName = 'legacy-admin'";
        var adminId = (long)(await userIdCommand.ExecuteScalarAsync())!;
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"");

        await SqliteLegacyDatabaseBootstrapper.PrepareForMigrationsAsync(db);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        Assert.Contains(InitialMigration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20260927155943_AddLoginLockout", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal("Legacy admin", await db.Users.Where(x => x.Id == adminId).Select(x => x.DisplayName).SingleAsync());
        Assert.Equal(0, (await db.Users.SingleAsync(x => x.Id == adminId)).FailedLoginAttempts);
    }

    [Fact]
    public async Task PrepareForMigrations_RejectsUnrecognizedSchemaWithoutChangingIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteLegacyDatabaseBootstrapper.PrepareForMigrationsAsync(db));

        Assert.Contains("No schema changes were made", error.Message);
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Products'";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }
}
