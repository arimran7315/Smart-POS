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
            await db.Database.MigrateAsync();
            var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
            db.Users.Add(admin);
            db.Settings.Add(new AppSetting { Key = "BackupLocation", Value = "D:\\PreviousBackups" });
            await db.SaveChangesAsync();
            var backups = new DatabaseBackupService(db);
            await backups.CreateBackupAsync(backupPath, admin.Id);
            var createdSettings = await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
            Assert.Equal(folder, createdSettings["BackupLocation"]);
            Assert.True(DateTimeOffset.TryParse(createdSettings["LastBackupUtc"], out _));
            Assert.Contains(await db.AuditEntries.ToListAsync(), entry => entry.Action == "Backup");
            db.Customers.Add(new Customer { Name = "Added after backup" }); await db.SaveChangesAsync();
            var restoreActor = new AppUser { UserName = "restore-actor", DisplayName = "Restore Actor", PasswordHash = "test-hash", Role = "Administrator" };
            db.Users.Add(restoreActor);
            await db.SaveChangesAsync();
            await backups.RestoreAsync(backupPath, restoreActor.Id);
            db.ChangeTracker.Clear();
            Assert.False(await db.Customers.AnyAsync());
            Assert.Single(Directory.GetFiles(folder, "smartpos.db.before-restore-*.bak"));
            var restoredSettings = await db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
            Assert.Equal("D:\\PreviousBackups", restoredSettings["BackupLocation"]);
            Assert.True(DateTimeOffset.TryParse(restoredSettings["LastBackupUtc"], out _));
            var audits = await db.AuditEntries.AsNoTracking().ToListAsync();
            Assert.Contains(audits, entry => entry.Action == "Restore" && entry.UserId is null);
            Assert.Contains(audits, entry => entry.Action == "Backup" && entry.UserId is null &&
                entry.Description == "Created an automatic safety backup before restore.");
            Assert.False(await db.Users.AnyAsync(x => x.Id == restoreActor.Id));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Restore_RejectsInvalidBackupBeforeChangingLiveDatabase()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SmartPOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "smartpos.db");
        var invalidBackupPath = Path.Combine(folder, "invalid.db");
        try
        {
            var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new PosDbContext(options);
            await db.Database.MigrateAsync();
            var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
            db.Users.Add(admin);
            db.Customers.Add(new Customer { Name = "Keep this customer" });
            await db.SaveChangesAsync();
            await File.WriteAllTextAsync(invalidBackupPath, "not a SQLite database");

            await Assert.ThrowsAsync<PosRuleException>(() => new DatabaseBackupService(db).RestoreAsync(invalidBackupPath, admin.Id));
            Assert.True(await db.Customers.AnyAsync(x => x.Name == "Keep this customer"));
            Assert.Empty(Directory.GetFiles(folder, "smartpos.db.before-restore-*.bak"));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
