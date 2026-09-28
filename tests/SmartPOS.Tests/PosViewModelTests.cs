using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.App.ViewModels;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PosViewModelTests
{
    [Fact]
    public async Task AddingSingleProductImmediatelyRecalculatesCheckoutTotals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "single-item-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);

        Assert.Equal("", viewModel.AddProduct(new SaleProductLookup(1, "Milk", 200, 5, 0, 10)));

        Assert.Equal(200m, viewModel.Subtotal);
        Assert.Equal(10m, viewModel.TaxTotal);
        Assert.Equal(210m, viewModel.GrandTotal);
        Assert.True(viewModel.CanComplete);
    }

    [Fact]
    public async Task ProductSearchCommandAddsUniqueMatchAndRequiresSelectionForAmbiguousNames()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-search-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var colaSmall = new Product { Name = "Cola", Sku = "COLA-S", Barcode = "10001", CurrentStock = 4, SalePrice = 50 };
        var colaLarge = new Product { Name = "Cola", Sku = "COLA-L", Barcode = "10002", CurrentStock = 2, SalePrice = 90 };
        db.AddRange(cashier, colaSmall, colaLarge);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);

        viewModel.ProductSearchText = "COLA-S";
        await viewModel.SearchProductsCommand.ExecuteAsync();

        Assert.Equal(colaSmall.Id, Assert.Single(viewModel.CartLines).ProductId);
        Assert.Equal("", viewModel.ProductSearchText);
        viewModel.CartLines.Clear();

        viewModel.ProductSearchText = "Cola";
        await viewModel.SearchProductsCommand.ExecuteAsync();

        Assert.Empty(viewModel.CartLines);
        Assert.Equal(2, viewModel.ProductMatches.Count);
        Assert.True(viewModel.HasMultipleProductMatches);
        Assert.Contains("Select the correct product", viewModel.StatusMessage);
        Assert.Contains(viewModel.ProductMatches, match => match.Sku == "COLA-S" && match.Barcode == "10001");
        viewModel.SelectedProductMatch = viewModel.ProductMatches.Single(match => match.Sku == "COLA-L");
        await viewModel.AddSelectedProductCommand.ExecuteAsync();

        Assert.Equal(colaLarge.Id, Assert.Single(viewModel.CartLines).ProductId);
        Assert.Empty(viewModel.ProductMatches);
        Assert.False(viewModel.HasMultipleProductMatches);
        Assert.Equal("", viewModel.ProductSearchText);
    }

    [Fact]
    public async Task ProductSearchCommandReportsUnknownAndOutOfStockBarcodesWithoutAddingItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-barcode-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var emptyProduct = new Product { Name = "Empty shelf item", Sku = "EMPTY-1", Barcode = "20001", CurrentStock = 0, SalePrice = 25 };
        db.AddRange(cashier, emptyProduct);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);

        viewModel.ProductSearchText = "99999";
        await viewModel.SearchProductsCommand.ExecuteAsync();
        Assert.Contains("No active product", viewModel.StatusMessage);
        Assert.Empty(viewModel.CartLines);

        viewModel.ProductSearchText = "20001";
        await viewModel.SearchProductsCommand.ExecuteAsync();
        Assert.Contains("out of stock", viewModel.StatusMessage);
        Assert.Empty(viewModel.CartLines);
    }

    [Fact]
    public async Task AddingResumedHeldSaleItemImmediatelyRecalculatesCheckoutTotals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "held-item-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);

        viewModel.AddHeldSaleItem(new HeldSaleProduct(1, "Milk", 1, 200, 5, 0, 10));

        Assert.Equal(200m, viewModel.Subtotal);
        Assert.Equal(10m, viewModel.TaxTotal);
        Assert.Equal(210m, viewModel.GrandTotal);
        Assert.True(viewModel.CanComplete);
    }

    [Fact]
    public async Task CheckoutTotalsAndTenderFeedbackFollowBoundCartAndPaymentState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-vm-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();

        var viewModel = CreateViewModel(db, cashier);
        Assert.Equal("", viewModel.AddProduct(new SaleProductLookup(1, "Milk", 200, 5, 0, 10)));
        Assert.Equal("", viewModel.AddProduct(new SaleProductLookup(1, "Milk", 200, 5, 0, 10)));
        Assert.Equal(400m, viewModel.Subtotal);
        Assert.Equal(20m, viewModel.TaxTotal);
        Assert.Equal(420m, viewModel.GrandTotal);
        Assert.True(viewModel.CanComplete);

        viewModel.PaymentMethod = "Cash";
        viewModel.PaidText = "450";
        Assert.Equal("Change: 30.00", viewModel.ChangeSummary);
        viewModel.PaymentMethod = "Mixed";
        viewModel.MixedCashText = "200";
        viewModel.MixedCardText = "220";
        Assert.Equal("Remaining: 0.00", viewModel.ChangeSummary);

        viewModel.BillDiscountText = "500";
        Assert.False(viewModel.CanComplete);
        Assert.NotEmpty(viewModel.DiscountError);
        viewModel.BillDiscountText = "0";
        viewModel.CartLines[0].Quantity = 3;
        Assert.Equal(630m, viewModel.GrandTotal);
        Assert.Equal("Remaining: 210.00", viewModel.ChangeSummary);
    }

    [Fact]
    public async Task DecliningPosCancelCommandKeepsDraftAndDoesNotWriteAudit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-cancel-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();
        var confirmation = new StubConfirmation(false);
        var viewModel = new PosViewModel(new ProductService(db), new HeldSaleService(db), new SaleService(db),
            new PosScreenService(db), cashier, confirmation);
        viewModel.AddProduct(new SaleProductLookup(1, "Milk", 200, 5, 0, 10));

        await viewModel.CancelSaleCommand.ExecuteAsync();

        Assert.Single(viewModel.CartLines);
        Assert.Equal(0, await db.AuditEntries.CountAsync());
        Assert.Equal(1, confirmation.Calls);
    }

    [Fact]
    public async Task PosHoldAndResumeCommandsRefreshAndRestoreSaleState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-hold-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Administrator" };
        var product = new Product { Name = "Milk", Sku = "POS-HOLD-MILK", CurrentStock = 5, SalePrice = 200, PurchasePrice = 100, TaxRate = 5 };
        db.AddRange(cashier, product);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);
        Assert.Equal("", viewModel.AddProduct(new SaleProductLookup(product.Id, product.Name, product.SalePrice, product.TaxRate, 0, product.CurrentStock)));
        viewModel.SaleNotes = "Customer returning";
        viewModel.BillDiscountText = "5";

        await viewModel.HoldSaleCommand.ExecuteAsync();

        Assert.Empty(viewModel.CartLines);
        Assert.True(viewModel.HeldSales.Count == 1,
            $"Status={viewModel.StatusMessage}; persisted={await db.HeldSales.CountAsync()}; userId={cashier.Id}");
        var held = Assert.Single(viewModel.HeldSales);
        Assert.Contains(held.DisplayLabel, viewModel.StatusMessage);
        viewModel.SelectedHeldSale = held;

        await viewModel.ResumeHeldSaleCommand.ExecuteAsync();

        Assert.Single(viewModel.CartLines);
        Assert.Equal(product.Id, viewModel.CartLines[0].ProductId);
        Assert.Equal("Customer returning", viewModel.SaleNotes);
        Assert.Equal("5", viewModel.BillDiscountText);
        Assert.Equal(held.Id, viewModel.ResumedHeldSaleId);
        Assert.Equal("Sale resumed.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PosCompleteCommandPersistsSaleAndExposesReceiptTarget()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var cashier = new AppUser { UserName = "pos-complete-cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var product = new Product { Name = "Bread", Sku = "POS-COMPLETE-BREAD", CurrentStock = 4, SalePrice = 100, PurchasePrice = 60, TaxRate = 5 };
        db.AddRange(cashier, product);
        await db.SaveChangesAsync();
        var viewModel = CreateViewModel(db, cashier);
        Assert.Equal("", viewModel.AddProduct(new SaleProductLookup(product.Id, product.Name, product.SalePrice, product.TaxRate, 0, product.CurrentStock)));
        viewModel.PaidText = "105";

        await viewModel.CompleteSaleCommand.ExecuteAsync();

        Assert.Empty(viewModel.CartLines);
        Assert.NotNull(viewModel.LastSale);
        Assert.True(viewModel.CanPrintLastReceipt);
        Assert.Equal("Completed", viewModel.LastSale.Status);
        Assert.Equal(3m, product.CurrentStock);
        Assert.Equal(1, await db.Sales.CountAsync());
        Assert.Contains(viewModel.LastSale.InvoiceNumber, viewModel.StatusMessage);
    }

    private static PosViewModel CreateViewModel(PosDbContext db, AppUser user) =>
        new(new ProductService(db), new HeldSaleService(db), new SaleService(db), new PosScreenService(db), user,
            new ConfirmAllService());

    private sealed class ConfirmAllService : IUserConfirmationService
    {
        public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question) => Task.FromResult(true);
    }

    private sealed class StubConfirmation(bool answer) : IUserConfirmationService
    {
        public int Calls { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}
