using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PosWorkflowTests
{
    [Fact]
    public async Task SalePurchaseAndReturn_UpdateStockAndKeepInvoiceHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test-hash", Role = "Cashier" };
        var product = new Product { Name = "Test item", Sku = "T-01", CurrentStock = 5, PurchasePrice = 50, SalePrice = 100, TaxRate = 5 };
        var supplier = new Supplier { Name = "Test supplier" };
        var customer = new Customer { Name = "Test customer", CreditLimit = 100 };
        db.AddRange(admin, cashier, product, supplier, customer); await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PurchaseService(db).ReceiveAsync(supplier.Id, cashier.Id, [(product.Id, 1m, 50m)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SaleService(db).CompleteAsync([(product.Id, 6m)], admin.Id, null, 0, 630, "Cash", null));
        Assert.Equal(5m, product.CurrentStock);

        var saleService = new SaleService(db);
        var sale = await saleService.CompleteAsync([(product.Id, 1m)], admin.Id, null, 0, 105, "Cash", null);
        Assert.StartsWith($"INV-{DateTime.UtcNow:yyyy}-", sale.InvoiceNumber);
        Assert.Equal(4m, product.CurrentStock);

        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 2m, 70m)]);
        Assert.StartsWith($"PUR-{DateTime.UtcNow:yyyy}-", purchase.InvoiceNumber);
        Assert.Equal(6m, product.CurrentStock);

        var returned = await new ReturnsService(db).ReturnAsync(sale.Id, admin.Id, [(sale.Items[0].Id, 1m)], "Customer return");
        Assert.Equal(105m, returned.Total);
        Assert.Equal(7m, product.CurrentStock);
        Assert.Equal(0m, sale.PaidAmount);
        Assert.Equal("Returned", sale.Status);
        Assert.Equal(-105m, await db.Payments.Where(x => x.SaleId == sale.Id && x.Method == "Refund").Select(x => x.Amount).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => saleService.CompleteAsync([(product.Id, 1m)], admin.Id, customer.Id, 0, 0, "Credit", null));
        Assert.Equal(7m, product.CurrentStock);
        customer.CreditLimit = 500;
        var creditSale = await saleService.CompleteAsync([(product.Id, 1m)], admin.Id, customer.Id, 0, 0, "Credit", null);
        Assert.Equal(105m, creditSale.RemainingAmount);
        await new PaymentService(db).ReceiveCreditPaymentAsync(creditSale.Id, 50, admin.Id, "Cash");
        Assert.Equal(55m, creditSale.RemainingAmount);
        var dashboard = await new DashboardService(db).GetTodayAsync(admin.Id);
        Assert.Equal(105m, dashboard.TodaySales);
        Assert.Equal(30m, dashboard.TodayProfit);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var reports = new ReportsService(db);
        Assert.Equal(105m, Assert.Single(await reports.GetDailySalesAsync(admin.Id, today, today)).SalesTotal);
        Assert.Equal(1m, Assert.Single(await reports.GetTopProductsAsync(admin.Id, today, today)).UnitsSold);
        Assert.Equal(30m, Assert.Single(await reports.GetDailyProfitAsync(admin.Id, today, today)).Profit);
        Assert.Equal(4, await db.StockMovements.CountAsync());
    }
}
