using Microsoft.EntityFrameworkCore;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class StockAdjustmentViewModelTests
{
    [Fact]
    public async Task AdjustmentCommand_RequiresInventoryPermission()
    {
        await using var db = await CreateDatabaseAsync();
        var (cashier, product) = await SeedAsync(db, "Cashier");
        var viewModel = CreateViewModel(db, cashier, new RecordingConfirmationService(accept: true));
        viewModel.SelectedProduct = product;

        Assert.False(viewModel.AdjustStockCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeclinedAdjustmentLeavesStockAndHistoryUnchanged()
    {
        await using var db = await CreateDatabaseAsync();
        var (admin, product) = await SeedAsync(db, "Administrator");
        var confirmation = new RecordingConfirmationService(accept: false);
        var viewModel = CreateViewModel(db, admin, confirmation);
        viewModel.SelectedProduct = product;
        viewModel.AdjustmentQuantity = "-2";
        viewModel.AdjustmentReason = "Damaged";

        await viewModel.AdjustStockCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Contains("Coffee", prompt.Message);
        Assert.Contains("-2", prompt.Message);
        Assert.Contains("Damaged", prompt.Message);
        db.ChangeTracker.Clear();
        Assert.Equal(5m, (await db.Products.SingleAsync(x => x.Id == product.Id)).CurrentStock);
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(0, await db.AuditEntries.CountAsync(x => x.Action == "Adjust"));
    }

    [Fact]
    public async Task AcceptedAdjustmentUpdatesStockAndCreatesMovement()
    {
        await using var db = await CreateDatabaseAsync();
        var (admin, product) = await SeedAsync(db, "Administrator");
        var confirmation = new RecordingConfirmationService(accept: true);
        var viewModel = CreateViewModel(db, admin, confirmation);
        viewModel.SelectedProduct = product;
        viewModel.AdjustmentQuantity = "3";
        viewModel.AdjustmentReason = "Opening count correction";

        await viewModel.AdjustStockCommand.ExecuteAsync();

        Assert.Single(confirmation.Requests);
        db.ChangeTracker.Clear();
        Assert.Equal(8m, (await db.Products.SingleAsync(x => x.Id == product.Id)).CurrentStock);
        var movement = Assert.Single(await db.StockMovements.ToListAsync());
        Assert.Equal(3m, movement.QuantityDelta);
        Assert.Equal(8m, movement.BalanceAfter);
        Assert.Equal("Opening count correction", movement.Reason);
        Assert.Contains(await db.AuditEntries.ToListAsync(), x => x.Action == "Adjust");
    }

    private static ProductsViewModel CreateViewModel(PosDbContext db, AppUser user, IUserConfirmationService confirmation) =>
        new(new ProductService(db), new InventoryService(db), new CatalogTaxonomyService(db), user.Id, user.Role, confirmation);

    private static async Task<PosDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new PosDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<(AppUser User, Product Product)> SeedAsync(PosDbContext db, string role)
    {
        var user = new AppUser { UserName = role, DisplayName = role, PasswordHash = "test-hash", Role = role };
        var product = new Product { Name = "Coffee", Sku = $"COFFEE-{role}", CurrentStock = 5, SalePrice = 10 };
        db.AddRange(user, product);
        await db.SaveChangesAsync();
        return (user, product);
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
