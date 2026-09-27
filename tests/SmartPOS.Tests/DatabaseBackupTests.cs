using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class DatabaseBackupTests
{
    [Fact]
    public async Task Restore_ReplacesDatabaseAndCreatesSafetyCopy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SmartPOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "smartpos.db");
        var backupPath = Path.Combine(folder, "backup.db");
        try
        {
            var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new PosDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
            db.Users.Add(admin); await db.SaveChangesAsync();
            var backups = new DatabaseBackupService(db);
            await backups.CreateBackupAsync(backupPath, admin.Id);
            db.Customers.Add(new Customer { Name = "Added after backup" }); await db.SaveChangesAsync();
            await backups.RestoreAsync(backupPath, admin.Id);
            db.ChangeTracker.Clear();
            Assert.False(await db.Customers.AnyAsync());
            Assert.Single(Directory.GetFiles(folder, "smartpos.db.before-restore-*.bak"));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
