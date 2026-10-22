using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class QuotationWorkflowTests
{
    private static async Task<(PosDbContext db, AppUser admin, Product product, Customer customer)> SeedAsync(SqliteConnection connection)
    {
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var product = new Product { Name = "Widget", Sku = "W-1", CurrentStock = 10, PurchasePrice = 40, SalePrice = 100, TaxRate = 0 };
        var customer = new Customer { Name = "Acme", CreditLimit = 1000 };
        db.AddRange(admin, product, customer);
        await db.SaveChangesAsync();
        return (db, admin, product, customer);
    }

    [Fact]
    public async Task CreateQuotation_DoesNotDeductStock_AndConvertCreatesSale()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var (db, admin, product, customer) = await SeedAsync(connection);
        await using var _ = db;
        var quotes = new QuotationService(db);

        var quote = await quotes.CreateAsync(customer.Id, [new QuotationLineInput(product.Id, 2, null)], 0, "Please review", new DateOnly(2026, 12, 31), admin.Id);
        Assert.Equal("Open", quote.Status);
        Assert.Equal(200m, quote.GrandTotal);
        Assert.Equal(10m, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).CurrentStock);

        var items = await quotes.GetItemsAsync(quote.Id, admin.Id);
        Assert.Single(items);

        var sale = await quotes.ConvertAsync(quote.Id, 200m, "Cash", admin.Id, new SaleService(db));
        Assert.Equal(200m, sale.GrandTotal);
        Assert.Equal(8m, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).CurrentStock);
        var reloaded = await db.Quotations.AsNoTracking().SingleAsync(x => x.Id == quote.Id);
        Assert.Equal("Converted", reloaded.Status);
        Assert.Equal(sale.Id, reloaded.ConvertedSaleId);

        await Assert.ThrowsAsync<PosRuleException>(() => quotes.ConvertAsync(quote.Id, 200m, "Cash", admin.Id, new SaleService(db)));
    }

    [Fact]
    public async Task CancelledQuotation_CannotConvert_AndQuotingRequiresPermission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var (db, admin, product, customer) = await SeedAsync(connection);
        await using var _ = db;
        var inventoryManager = new AppUser { UserName = "inv", DisplayName = "Inv", PasswordHash = "test", Role = "Inventory Manager" };
        db.Users.Add(inventoryManager);
        await db.SaveChangesAsync();
        var quotes = new QuotationService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            quotes.CreateAsync(customer.Id, [new QuotationLineInput(product.Id, 1, null)], 0, null, null, inventoryManager.Id));

        var quote = await quotes.CreateAsync(customer.Id, [new QuotationLineInput(product.Id, 1, null)], 0, null, null, admin.Id);
        await quotes.CancelAsync(quote.Id, admin.Id);
        Assert.Equal("Cancelled", (await db.Quotations.AsNoTracking().SingleAsync(x => x.Id == quote.Id)).Status);
        await Assert.ThrowsAsync<PosRuleException>(() => quotes.ConvertAsync(quote.Id, 100m, "Cash", admin.Id, new SaleService(db)));
    }

    [Fact]
    public async Task Search_FiltersByStatusAndQuery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var (db, admin, product, customer) = await SeedAsync(connection);
        await using var _ = db;
        var quotes = new QuotationService(db);
        var open = await quotes.CreateAsync(customer.Id, [new QuotationLineInput(product.Id, 1, null)], 0, null, null, admin.Id);
        var cancelled = await quotes.CreateAsync(customer.Id, [new QuotationLineInput(product.Id, 1, null)], 0, null, null, admin.Id);
        await quotes.CancelAsync(cancelled.Id, admin.Id);

        Assert.Single(await quotes.SearchAsync(admin.Id, null, "Open"));
        Assert.Single(await quotes.SearchAsync(admin.Id, null, "Cancelled"));
        Assert.Equal(2, (await quotes.SearchAsync(admin.Id, null, "All")).Count);
        Assert.Contains(await quotes.SearchAsync(admin.Id, open.QuoteNumber, "All"), x => x.Id == open.Id);
        Assert.Contains(await quotes.SearchAsync(admin.Id, "Acme", "All"), x => x.Id == open.Id);
    }
}
