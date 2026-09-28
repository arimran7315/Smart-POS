using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ThemeSettingTests
{
    [Fact]
    public async Task ThemeSettingAcceptsLightOrDarkAndRejectsUnsupportedValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();
        var settings = new SettingsService(db);

        await settings.SetAsync("theme", "dark", admin.Id);
        Assert.Equal("Dark", await db.Settings.Where(x => x.Key == "Theme").Select(x => x.Value).SingleAsync());
        await settings.SetAsync("THEME", "LIGHT", admin.Id);
        Assert.Equal("Light", await db.Settings.Where(x => x.Key == "Theme").Select(x => x.Value).SingleAsync());
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("Theme", "System", admin.Id));
        Assert.Equal("Light", await db.Settings.Where(x => x.Key == "Theme").Select(x => x.Value).SingleAsync());
    }

    [Fact]
    public async Task ApplicationNameSettingIsCanonicalizedAndValidated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();
        var settings = new SettingsService(db);

        await settings.SetAsync("applicationname", "  Northside Market POS  ", admin.Id);
        Assert.Equal("Northside Market POS", await db.Settings.Where(x => x.Key == "ApplicationName").Select(x => x.Value).SingleAsync());
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("ApplicationName", "", admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("ApplicationName", new string('x', 65), admin.Id));
        Assert.Equal("Northside Market POS", await db.Settings.Where(x => x.Key == "ApplicationName").Select(x => x.Value).SingleAsync());
    }

    [Fact]
    public async Task SettingsRejectUnknownKeysAndValidateReceiptConfiguration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        await db.Users.AddAsync(admin);
        await db.SaveChangesAsync();
        var settings = new SettingsService(db);

        await settings.SetAsync("receiptwidthmm", "58", admin.Id);
        await settings.SetAsync("receiptprintmode", "escpos", admin.Id);
        await settings.SetAsync("purchasereturnprefix", "  SUP-RET  ", admin.Id);
        Assert.Equal("58", await db.Settings.Where(x => x.Key == "ReceiptWidthMm").Select(x => x.Value).SingleAsync());
        Assert.Equal("EscPos", await db.Settings.Where(x => x.Key == "ReceiptPrintMode").Select(x => x.Value).SingleAsync());
        Assert.Equal("SUP-RET", await db.Settings.Where(x => x.Key == "PurchaseReturnPrefix").Select(x => x.Value).SingleAsync());
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("ReceiptWidthMm", "300", admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("ReceiptPrintMode", "RawDanger", admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("LastBackupUtc", "2000-01-01", admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => settings.SetAsync("UnexpectedRuntimeKey", "value", admin.Id));
        Assert.Equal(3, await db.Settings.CountAsync());
    }
}
