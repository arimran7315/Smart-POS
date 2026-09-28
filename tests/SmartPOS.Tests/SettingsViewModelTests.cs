using Microsoft.EntityFrameworkCore;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task SaveCommandPersistsCanonicalSettingAndRaisesRuntimeUpdateEvent()
    {
        await using var db = await CreateDatabaseAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var viewModel = new SettingsViewModel(new SettingsService(db), admin, supportsBackup: true);
        SettingSavedEventArgs? applied = null;
        viewModel.SettingSaved += (_, setting) => applied = setting;
        viewModel.SettingKey = " applicationname ";
        viewModel.SettingValue = "  Northside POS  ";

        await viewModel.SaveCommand.ExecuteAsync();

        Assert.Equal("Northside POS", await db.Settings.Where(x => x.Key == "ApplicationName").Select(x => x.Value).SingleAsync());
        Assert.Equal("ApplicationName", Assert.Single(viewModel.Settings).Key);
        Assert.Equal(new SettingSavedEventArgs("applicationname", "Northside POS"), applied);
        Assert.Equal("Saved setting applicationname.", viewModel.Status);
    }

    [Fact]
    public void SaveCommandRequiresSettingsManagePermission()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new PosDbContext(options);
        var manager = new AppUser { Id = 7, UserName = "manager", DisplayName = "Manager", Role = "Manager" };
        var viewModel = new SettingsViewModel(new SettingsService(db), manager, supportsBackup: true);

        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.NewCommand.CanExecute(null));
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    private static async Task<PosDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new PosDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
