using Microsoft.EntityFrameworkCore;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class UserManagementViewModelTests
{
    [Fact]
    public async Task SelectingAnotherAccountAndStartingNewAccountClearPassword()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var db = new PosDbContext(options);
        var actor = new AppUser { Id = 1, UserName = "admin", DisplayName = "Admin", Role = "Administrator" };
        var viewModel = new UserManagementViewModel(new UserManagementService(db, new PasswordHasher()), actor);

        viewModel.Password = "temporary-password";
        viewModel.SelectedUser = new UserAccountSummary(2, "cashier", "Cashier", "Cashier", true, DateTime.UtcNow);

        Assert.Empty(viewModel.Password);

        viewModel.Password = "another-temporary-password";
        await viewModel.NewCommand.ExecuteAsync();

        Assert.Empty(viewModel.Password);
        Assert.Null(viewModel.SelectedUser);
    }
}
