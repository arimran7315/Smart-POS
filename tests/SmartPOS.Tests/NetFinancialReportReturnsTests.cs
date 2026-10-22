using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class NetFinancialReportReturnsTests
{
    [Fact]
    public async Task CashierCustomerAndSupplierSummariesSubtractReturnsOnReturnDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var cashier = new AppUser { UserName = "original-cashier", DisplayName = "Original Cashier", PasswordHash = "hash", Role = "Administrator" };
        var customer = new Customer { Name = "Original Customer" };
        var supplier = new Supplier { Name = "Original Supplier" };
        db.AddRange(cashier, customer, supplier);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayUtc = LocalNoon(today);
        var oldSale = new Sale
        {
            InvoiceNumber = "OLD-SALE-RETURN", UserId = cashier.Id, CustomerId = customer.Id,
            SoldAtUtc = todayUtc.AddDays(-5), Subtotal = 100, GrandTotal = 100
        };
        var oldPurchase = new Purchase
        {
            InvoiceNumber = "OLD-PURCHASE-RETURN", SupplierId = supplier.Id, UserId = cashier.Id,
            PurchasedAtUtc = todayUtc.AddDays(-5), Total = 200
        };
        db.AddRange(oldSale, oldPurchase);
        await db.SaveChangesAsync();
        db.Returns.Add(new SaleReturn
        {
            ReturnNumber = "TODAY-SALE-RETURN", SaleId = oldSale.Id, UserId = cashier.Id,
            ReturnedAtUtc = todayUtc, Total = 30, Reason = "Returned today"
        });
        db.PurchaseReturns.Add(new PurchaseReturn
        {
            ReturnNumber = "TODAY-PURCHASE-RETURN", PurchaseId = oldPurchase.Id, UserId = cashier.Id,
            ReturnedAtUtc = todayUtc, Total = 40, Reason = "Returned today"
        });
        await db.SaveChangesAsync();

        var reports = new ReportsService(db);
        var cashierRow = Assert.Single(await reports.GetSalesByCashierAsync(cashier.Id, today, today));
        var customerRow = Assert.Single(await reports.GetSalesByCustomerAsync(cashier.Id, today, today));
        var supplierRow = Assert.Single(await reports.GetPurchasesBySupplierAsync(cashier.Id, today, today));

        Assert.Equal("Original Cashier", cashierRow.CashierName);
        Assert.Equal(-30m, cashierRow.Sales);
        Assert.Equal(0, cashierRow.SaleCount);
        Assert.Equal("Original Customer", customerRow.CustomerName);
        Assert.Equal(-30m, customerRow.Sales);
        Assert.Equal(0, customerRow.SaleCount);
        Assert.Equal("Original Supplier", supplierRow.SupplierName);
        Assert.Equal(-40m, supplierRow.Purchases);
        Assert.Equal(0, supplierRow.PurchaseCount);
    }

    private static DateTime LocalNoon(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local);
    }
}
