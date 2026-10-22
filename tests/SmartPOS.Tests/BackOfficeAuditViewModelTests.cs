using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class BackOfficeAuditViewModelTests
{
    [Fact]
    public async Task InitializeLoadsAuditEntriesIntoBoundCollectionForAuthorizedUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "audit-admin", DisplayName = "Audit admin", PasswordHash = "test", Role = "Administrator" };
        db.Add(admin);
        await db.SaveChangesAsync();
        db.AuditEntries.Add(new AuditEntry { UserId = admin.Id, Action = "Create", EntityName = "Product", EntityId = "42", Description = "Created a test product." });
        await db.SaveChangesAsync();

        var viewModel = new BackOfficeViewModel(new CustomerService(db), new SupplierService(db), new ExpenseService(db),
            new PaymentService(db), new CustomerAccountPaymentService(db), new SupplierPaymentService(db),
            new BackOfficeQueryService(db), new AuditService(db), admin, new AcceptConfirmationService());

        await viewModel.InitializeAsync();

        var entry = Assert.Single(viewModel.RecentAuditEntries);
        Assert.Equal("Create", entry.Action);
        Assert.Equal("Product", entry.EntityName);
        Assert.Equal("42", entry.EntityId);
    }

    private sealed class AcceptConfirmationService : IUserConfirmationService
    {
        public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question) => Task.FromResult(true);
    }
}
