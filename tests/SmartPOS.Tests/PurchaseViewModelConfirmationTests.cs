using Microsoft.EntityFrameworkCore;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PurchaseViewModelConfirmationTests
{
    [Fact]
    public async Task ReceiveCommand_AsksBeforeIncreasingStockAndHonorsCancellation()
    {
        await using var db = await CreateDatabaseAsync();
        var confirmation = new RecordingConfirmationService(accept: false);
        var viewModel = CreateViewModel(db, confirmation);
        viewModel.SelectedSupplier = new Supplier { Id = 1, Name = "Acme Supply" };
        viewModel.Lines.Add(new PurchaseLineRow(1, "Coffee", 2, 10, 0, 0));

        await viewModel.ReceiveCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Equal("Confirm purchase", prompt.Title);
        Assert.Contains("Acme Supply", prompt.Message);
        Assert.Contains("20", prompt.Message);
        Assert.Equal(ConfirmationKind.Question, prompt.Kind);
        Assert.Equal(0, await db.Purchases.CountAsync());
    }

    [Fact]
    public async Task ReturnCommand_AsksBeforeReducingStockAndHonorsCancellation()
    {
        await using var db = await CreateDatabaseAsync();
        var confirmation = new RecordingConfirmationService(accept: false);
        var viewModel = CreateViewModel(db, confirmation);
        viewModel.SelectedPurchase = new Purchase { Id = 1, InvoiceNumber = "PUR-RETURN" };
        viewModel.ReturnLines.Add(new PurchaseReturnLineRow(2, "Coffee", 1, 10));
        viewModel.ReturnReason = "Damaged";

        await viewModel.ReturnPurchaseCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Equal("Confirm purchase return", prompt.Title);
        Assert.Contains("PUR-RETURN", prompt.Message);
        Assert.Equal(ConfirmationKind.Warning, prompt.Kind);
        Assert.Equal(0, await db.PurchaseReturns.CountAsync());
    }

    private static PurchaseViewModel CreateViewModel(PosDbContext db, IUserConfirmationService confirmation) =>
        new(new ProductService(db), new SupplierService(db), new PurchaseService(db),
            new PurchaseReturnsService(db), new SupplierPaymentService(db),
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
