using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class FirstAdministratorTests
{
    [Fact]
    public async Task FirstAdministratorAndAuditEntryAreCreatedAndSetupCannotRepeat()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var hasher = new PasswordHasher();
        var authentication = new AuthenticationService(db, hasher);

        Assert.True(await authentication.IsFirstAdministratorRequiredAsync());
        var admin = await authentication.CreateFirstAdministratorAsync(" owner ", " Store Owner ", "a sufficiently long password");
        Assert.False(await authentication.IsFirstAdministratorRequiredAsync());
        Assert.Equal("owner", admin.UserName);
        Assert.Equal("Store Owner", admin.DisplayName);
        Assert.NotEqual("a sufficiently long password", admin.PasswordHash);
        Assert.True(hasher.Verify("a sufficiently long password", admin.PasswordHash));
        var audit = Assert.Single(await db.AuditEntries.Where(x => x.EntityName == "User").ToListAsync());
        Assert.Equal(admin.Id.ToString(), audit.EntityId);

        await Assert.ThrowsAsync<PosRuleException>(() => authentication.CreateFirstAdministratorAsync(
            "second", "Second Owner", "another sufficiently long password"));
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.AuditEntries.Where(x => x.EntityName == "User").ToListAsync());
    }
}
