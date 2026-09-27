using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task EmptyDatabaseIsCreatedFromCheckedInMigrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("20260927104303_InitialSqlite", await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await db.Products.CountAsync());
    }
}
