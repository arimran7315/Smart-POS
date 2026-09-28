using Microsoft.EntityFrameworkCore;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SalesHistoryConfirmationTests
{
    [Fact]
    public async Task ReturnCommand_AsksBeforeProcessingAndHonorsCancellation()
    {
        await using var db = await CreateDatabaseAsync();
        var confirmation = new RecordingConfirmationService(accept: false);
        var viewModel = CreateViewModel(db, confirmation);
        var sale = new Sale
        {
            Id = 1,
            InvoiceNumber = "INV-RETURN",
            Status = "Completed",
            Items = [new SaleItem { Id = 2, ProductName = "Coffee" }]
        };
        viewModel.SelectedSale = sale;
        viewModel.SelectedItem = sale.Items[0];

        await viewModel.ReturnCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Equal("Confirm return", prompt.Title);
        Assert.Contains("Coffee", prompt.Message);
        Assert.Equal(ConfirmationKind.Warning, prompt.Kind);
        Assert.Equal(0, await db.Returns.CountAsync());
    }

    [Fact]
    public async Task VoidCommand_AsksBeforeProcessingAndHonorsCancellation()
    {
        await using var db = await CreateDatabaseAsync();
        var confirmation = new RecordingConfirmationService(accept: false);
        var viewModel = CreateViewModel(db, confirmation);
        viewModel.SelectedSale = new Sale
        {
            Id = 1,
            InvoiceNumber = "INV-VOID",
            GrandTotal = 50,
            Status = "Completed"
        };
        viewModel.VoidReason = "Entered twice";

        await viewModel.VoidCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Equal("Confirm sale void", prompt.Title);
        Assert.Contains("INV-VOID", prompt.Message);
        Assert.Equal(ConfirmationKind.Warning, prompt.Kind);
        Assert.Equal(0, await db.AuditEntries.CountAsync());
    }

    private static SalesHistoryViewModel CreateViewModel(PosDbContext db, IUserConfirmationService confirmation) =>
        new(new SaleService(db), new ReturnsService(db),
            new AppUser { Id = 1, UserName = "admin", DisplayName = "Admin", Role = "Administrator" }, confirmation);

    private static async Task<PosDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new PosDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class RecordingConfirmationService(bool accept) : IUserConfirmationService
    {
        public List<ConfirmationRequest> Requests { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question)
        {
            Requests.Add(new(title, message, kind));
            return Task.FromResult(accept);
        }
    }

    private sealed record ConfirmationRequest(string Title, string Message, ConfirmationKind Kind);
}
