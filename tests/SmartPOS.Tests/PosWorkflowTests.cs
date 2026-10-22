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
    public async Task ProductService_PersistsImageAndStockRange_AndRejectsInvalidRange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "product-admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var observer = new AppUser { UserName = "product-observer", DisplayName = "Observer", PasswordHash = "test-hash", Role = "Observer" };
        db.AddRange(admin, observer);
        await db.SaveChangesAsync();

        var taxonomy = new CatalogTaxonomyService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => taxonomy.CategoriesAsync(observer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => taxonomy.BrandsAsync(observer.Id));
        Assert.Empty(await taxonomy.CategoriesAsync(admin.Id));
        Assert.Empty(await taxonomy.BrandsAsync(admin.Id));

        var service = new ProductService(db);
        var product = await service.SaveAsync(new Product
        {
            Name = "Image item", Sku = "IMG-1", PurchasePrice = 5, SalePrice = 10,
            MinimumStock = 2, MaximumStock = 20, ImagePath = "catalog/product-images/item.png"
        }, admin.Id);

        var saved = await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id);
        Assert.Equal(2, saved.MinimumStock);
        Assert.Equal(20, saved.MaximumStock);
        Assert.Equal("catalog/product-images/item.png", saved.ImagePath);

        product.MinimumStock = 21;
        await Assert.ThrowsAsync<PosRuleException>(() => service.SaveAsync(product, admin.Id));
        product.MinimumStock = 2;
        product.MaximumStock = -1;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveAsync(product, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => service.SaveAsync(new Product
        {
            Name = new string('P', 201), Sku = "LONG-NAME", PurchasePrice = 1, SalePrice = 2
        }, admin.Id));
    }

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

        var customerService = new CustomerService(db);
        Assert.Contains(await customerService.SearchAsync("Test", cashier.Id), x => x.Id == customer.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => customerService.SaveAsync(new Customer { Name = "Unauthorized edit" }, cashier.Id));
        Assert.Equal("Test customer", (await db.Customers.SingleAsync(x => x.Id == customer.Id)).Name);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new PurchaseService(db).ReceiveAsync(supplier.Id, cashier.Id, [(product.Id, 1m, 50m)]));
        await Assert.ThrowsAsync<PosRuleException>(() => new SaleService(db).CompleteAsync([(product.Id, 6m)], admin.Id, null, 0, 630, "Cash", null));
        Assert.Equal(5m, product.CurrentStock);

        var saleService = new SaleService(db);
        customer.CreditLimit = 500;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => saleService.CompleteAsync(
            [(product.Id, 1m)], cashier.Id, customer.Id, 0, 0, "Credit", null));
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Equal(5m, product.CurrentStock);
        customer.CreditLimit = 100;

        var sale = await saleService.CompleteAsync([(product.Id, 1m)], admin.Id, null, 0, 105, "Cash", "Customer requested a bag");
        Assert.StartsWith($"INV-{DateTime.UtcNow:yyyy}-", sale.InvoiceNumber);
        Assert.Equal("Customer requested a bag", sale.Notes);
        Assert.Equal(4m, product.CurrentStock);

        var purchases = new PurchaseService(db);
        product.IsActive = false;
        await Assert.ThrowsAsync<PosRuleException>(() => purchases.ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 2m, 70m)]));
        Assert.Equal(4m, product.CurrentStock);
        Assert.Empty(await db.Purchases.ToListAsync());
        product.IsActive = true;

        var purchase = await purchases.ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 2m, 70m)]);
        Assert.StartsWith($"PUR-{DateTime.UtcNow:yyyy}-", purchase.InvoiceNumber);
        Assert.Equal(6m, product.CurrentStock);

        var saleReturns = new ReturnsService(db);
        await Assert.ThrowsAsync<PosRuleException>(() => saleReturns.ReturnAsync(sale.Id, admin.Id, [(sale.Items[0].Id, 1m)], "  "));
        var returned = await saleReturns.ReturnAsync(sale.Id, admin.Id, [(sale.Items[0].Id, 1m)], "Customer return");
        Assert.Equal(105m, returned.Total);
        Assert.Equal(7m, product.CurrentStock);
        Assert.True(product.UpdatedAtUtc >= returned.ReturnedAtUtc);
        var returnMovement = await db.StockMovements.SingleAsync(x => x.Reference == returned.ReturnNumber);
        Assert.Equal("SaleReturn", returnMovement.Reason);
        Assert.Equal(1m, returnMovement.QuantityDelta);
        Assert.Equal(product.CurrentStock, returnMovement.BalanceAfter);
        Assert.Equal(0m, sale.PaidAmount);
        Assert.Equal("Returned", sale.Status);
        Assert.Equal(-105m, await db.Payments.Where(x => x.SaleId == sale.Id && x.Method == "Refund").Select(x => x.Amount).SingleAsync());
        await Assert.ThrowsAsync<PosRuleException>(() => saleService.CompleteAsync([(product.Id, 1m)], admin.Id, customer.Id, 0, 0, "Credit", null));
        Assert.Equal(7m, product.CurrentStock);
        customer.CreditLimit = 500;
        customer.OpeningBalance = 450;
        await Assert.ThrowsAsync<PosRuleException>(() => saleService.CompleteAsync([(product.Id, 1m)], admin.Id, customer.Id, 0, 0, "Credit", null));
        customer.OpeningBalance = 300;
        var creditSale = await saleService.CompleteAsync([(product.Id, 1m)], admin.Id, customer.Id, 0, 0, "Credit", null);
        Assert.Equal(105m, creditSale.RemainingAmount);
        await new PaymentService(db).ReceiveCreditPaymentAsync(creditSale.Id, 50, admin.Id, "Cash");
        Assert.Equal(55m, creditSale.RemainingAmount);
        var dashboard = await new DashboardService(db).GetTodayAsync(admin.Id);
        Assert.Equal(105m, dashboard.TodaySales);
        Assert.Equal(30m, dashboard.TodayProfit);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var reports = new ReportsService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.GetDailyProfitAsync(cashier.Id, today, today));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.GetSalesByCashierAsync(cashier.Id, today, today));
        Assert.Equal(105m, Assert.Single(await reports.GetDailySalesAsync(admin.Id, today, today)).SalesTotal);
        Assert.Equal(1m, Assert.Single(await reports.GetTopProductsAsync(admin.Id, today, today)).UnitsSold);
        Assert.Equal(30m, Assert.Single(await reports.GetDailyProfitAsync(admin.Id, today, today)).Profit);
        var cashierReport = Assert.Single(await reports.GetSalesByCashierAsync(admin.Id, today, today));
        Assert.Equal("Admin", cashierReport.CashierName);
        Assert.Equal(105m, cashierReport.Sales);
        Assert.Equal(2, cashierReport.SaleCount);
        Assert.Equal(2, (await reports.GetSalesByCustomerAsync(admin.Id, today, today)).Count);
        Assert.Equal(140m, Assert.Single(await reports.GetPurchasesByDayAsync(admin.Id, today, today)).NetPurchases);
        Assert.Equal(140m, Assert.Single(await reports.GetPurchasesBySupplierAsync(admin.Id, today, today)).Purchases);
        await new ExpenseService(db).RecordAsync("Shop supplies", "Operations", 25m, admin.Id);
        Assert.Equal(25m, Assert.Single(await reports.GetExpensesAsync(admin.Id, today, today)).Amount);
        var paymentRows = await reports.GetPaymentsAsync(admin.Id, today, today);
        Assert.Equal(50m, paymentRows.Sum(x => x.NetAmount));
        Assert.Equal(155m, paymentRows.Single(x => x.Method == "Cash").NetAmount);
        Assert.Equal(-105m, paymentRows.Single(x => x.Method == "Refund").NetAmount);
        Assert.Equal("Sale", Assert.Single(await reports.GetReturnsAsync(admin.Id, today, today)).Type);
        Assert.Equal(5m, Assert.Single(await reports.GetTaxByDayAsync(admin.Id, today, today)).NetTax);
        Assert.Equal(4, (await reports.GetStockMovementsAsync(admin.Id, today, today)).Count);
        var dashboardService = new DashboardService(db);
        var adminTrends = await dashboardService.GetLastSevenDaysAsync(admin.Id);
        Assert.Equal(7, adminTrends.Count);
        var todayTrend = Assert.Single(adminTrends, x => x.Day == today);
        Assert.Equal(105m, todayTrend.Sales);
        Assert.Equal(140m, todayTrend.Purchases);
        Assert.Equal(30m, todayTrend.Profit);
        Assert.All(await dashboardService.GetLastSevenDaysAsync(cashier.Id), point => Assert.Null(point.Profit));
        Assert.Equal(4, await db.StockMovements.CountAsync());
    }
}
