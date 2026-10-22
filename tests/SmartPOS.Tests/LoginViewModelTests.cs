using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task FirstRunViewModelValidatesAndCreatesTheInitialAdministrator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var hasher = new PasswordHasher();
        var viewModel = new LoginViewModel(new AuthenticationService(db, hasher));

        await viewModel.InitializeAsync("Corner Shop");
        Assert.True(viewModel.SetupRequired);
        Assert.Contains("first administrator", viewModel.ModeText);
        Assert.Equal("Create administrator", viewModel.ButtonText);
        viewModel.UserName = " owner ";
        viewModel.DisplayName = " Store Owner ";
        Assert.False(await viewModel.SubmitAsync("short", "short"));
        Assert.Contains("at least", viewModel.Status);
        Assert.False(await viewModel.SubmitAsync("long enough password", "different password"));
        Assert.Contains("do not match", viewModel.Status);

        var buttonLabels = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ButtonText)) buttonLabels.Add(viewModel.ButtonText);
        };
        Assert.True(await viewModel.SubmitAsync("a sufficiently long password", "a sufficiently long password"));
        Assert.Contains("Creating account…", buttonLabels);
        Assert.Equal("Create administrator", viewModel.ButtonText);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("owner", viewModel.CurrentUser?.UserName);
        Assert.Equal("Administrator", viewModel.CurrentUser?.Role);
        Assert.False(await viewModel.SubmitAsync("another sufficiently long password", "another sufficiently long password"));
    }

    [Fact]
    public async Task SignInViewModelReportsInvalidCredentialsAndReturnsAuthenticatedUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var hasher = new PasswordHasher();
        var cashier = new AppUser
        {
            UserName = "cashier",
            DisplayName = "Cashier",
            PasswordHash = hasher.Hash("a sufficiently long password"),
            Role = "Cashier"
        };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();
        var viewModel = new LoginViewModel(new AuthenticationService(db, hasher));

        await viewModel.InitializeAsync("SmartPOS");
        Assert.False(viewModel.SetupRequired);
        Assert.Equal("Sign in", viewModel.ButtonText);
        Assert.False(await viewModel.SubmitAsync("incorrect password"));
        Assert.Contains("incorrect", viewModel.Status);
        viewModel.UserName = " cashier ";
        var buttonLabels = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.ButtonText)) buttonLabels.Add(viewModel.ButtonText);
        };
        Assert.True(await viewModel.SubmitAsync("a sufficiently long password"));
        Assert.Contains("Signing in…", buttonLabels);
        Assert.Equal("Sign in", viewModel.ButtonText);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(cashier.Id, viewModel.CurrentUser?.Id);
    }
}
