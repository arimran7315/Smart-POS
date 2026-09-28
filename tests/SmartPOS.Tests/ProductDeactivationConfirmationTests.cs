using Microsoft.EntityFrameworkCore;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ProductDeactivationConfirmationTests
{
    [Fact]
    public async Task DeactivateProductCommand_LeavesProductActiveWhenConfirmationIsDeclined()
    {
        await using var db = await CreateDatabaseAsync();
        var admin = await AddAdminAsync(db);
        var product = new Product { Name = "Coffee", Sku = "COFFEE-1", IsActive = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var confirmation = new RecordingConfirmationService();
        var viewModel = CreateViewModel(db, admin, confirmation);
        viewModel.SelectedProduct = product;

        Assert.True(viewModel.DeactivateCommand.CanExecute(null));
        await viewModel.DeactivateCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Contains("Coffee", prompt.Message);
        db.ChangeTracker.Clear();
        Assert.True((await db.Products.SingleAsync(x => x.Id == product.Id)).IsActive);
    }

    [Fact]
    public async Task DeactivateCategoryCommand_LeavesCategoryActiveWhenConfirmationIsDeclined()
    {
        await using var db = await CreateDatabaseAsync();
        var admin = await AddAdminAsync(db);
        var category = new Category { Name = "Drinks", IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var confirmation = new RecordingConfirmationService();
        var viewModel = CreateViewModel(db, admin, confirmation);
        viewModel.SelectedCategory = category;

        Assert.True(viewModel.DeactivateCategoryCommand.CanExecute(null));
        await viewModel.DeactivateCategoryCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Contains("Drinks", prompt.Message);
        db.ChangeTracker.Clear();
        Assert.True((await db.Categories.SingleAsync(x => x.Id == category.Id)).IsActive);
    }

    [Fact]
    public async Task DeactivateBrandCommand_LeavesBrandActiveWhenConfirmationIsDeclined()
    {
        await using var db = await CreateDatabaseAsync();
        var admin = await AddAdminAsync(db);
        var brand = new Brand { Name = "Acme", IsActive = true };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        var confirmation = new RecordingConfirmationService();
        var viewModel = CreateViewModel(db, admin, confirmation);
        viewModel.SelectedBrand = brand;

        Assert.True(viewModel.DeactivateBrandCommand.CanExecute(null));
        await viewModel.DeactivateBrandCommand.ExecuteAsync();

        var prompt = Assert.Single(confirmation.Requests);
        Assert.Contains("Acme", prompt.Message);
        db.ChangeTracker.Clear();
        Assert.True((await db.Brands.SingleAsync(x => x.Id == brand.Id)).IsActive);
    }

    private static ProductsViewModel CreateViewModel(PosDbContext db, AppUser admin, IUserConfirmationService confirmation) =>
        new(new ProductService(db), new InventoryService(db), new CatalogTaxonomyService(db), admin.Id, admin.Role, confirmation);

    private static async Task<PosDbContext> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options;
        var db = new PosDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<AppUser> AddAdminAsync(PosDbContext db)
    {
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private sealed class RecordingConfirmationService : IUserConfirmationService
    {
        public List<ConfirmationRequest> Requests { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question)
        {
            Requests.Add(new(title, message, kind));
            return Task.FromResult(false);
        }
    }

    private sealed record ConfirmationRequest(string Title, string Message, ConfirmationKind Kind);
}
