using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class LedgerReportTests
{
    [Fact]
    public async Task LedgersIncludeOpeningBalancesSalesPurchasesPaymentsAndReturns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test-hash", Role = "Administrator" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test-hash", Role = "Manager" };
        var customer = new Customer { Name = "Ledger customer", OpeningBalance = 120 };
        var supplier = new Supplier { Name = "Ledger supplier", OpeningBalance = 30 };
        var product = new Product { Name = "Ledger item", Sku = "LEDGER", CurrentStock = 10, SalePrice = 100, PurchasePrice = 40 };
        db.AddRange(admin, manager, customer, supplier, product);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var soldAt = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(new TimeOnly(12, 0)), TimeZoneInfo.Local);
        var sale = new Sale
        {
            InvoiceNumber = "LEDGER-SALE", CustomerId = customer.Id, UserId = admin.Id, SoldAtUtc = soldAt,
            Subtotal = 100, GrandTotal = 100, PaidAmount = 20, RemainingAmount = 80,
            Items = [new SaleItem { ProductId = product.Id, ProductName = product.Name, Quantity = 1, UnitPrice = 100, UnitCost = 40, Total = 100 }],
            Payments = [new Payment { Amount = 20, Method = "Cash", PaidAtUtc = soldAt }]
        };
        db.Sales.Add(sale);
        var purchase = new Purchase
        {
            InvoiceNumber = "LEDGER-PURCHASE", SupplierId = supplier.Id, UserId = admin.Id, PurchasedAtUtc = soldAt, Total = 80,
            Items = [new PurchaseItem { ProductId = product.Id, Quantity = 2, UnitCost = 40, Total = 80 }]
        };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();
        db.Returns.Add(new SaleReturn
        {
            ReturnNumber = "LEDGER-RETURN", SaleId = sale.Id, UserId = admin.Id, ReturnedAtUtc = soldAt, Total = 50,
            Reason = "Partial return", Items = [new SaleReturnItem { SaleItemId = sale.Items[0].Id, ProductId = product.Id, Quantity = .5m, Refund = 50 }]
        });
        sale.RemainingAmount = 30;
        db.PurchaseReturns.Add(new PurchaseReturn
        {
            ReturnNumber = "LEDGER-PURCHASE-RETURN", PurchaseId = purchase.Id, UserId = admin.Id, ReturnedAtUtc = soldAt,
            Total = 40, Reason = "Partial return", Items = [new PurchaseReturnItem { PurchaseItemId = purchase.Items[0].Id, ProductId = product.Id, Quantity = 1, Total = 40 }]
        });
        await db.SaveChangesAsync();
        var supplierPayments = new SupplierPaymentService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => supplierPayments.RecordAsync(supplier.Id, 10m, manager.Id, "Cash"));
        await supplierPayments.RecordAsync(supplier.Id, 10m, admin.Id, "Cash", "SUP-PAY-1");
        var customerPayments = new CustomerAccountPaymentService(db);
        await Assert.ThrowsAsync<PosRuleException>(() => customerPayments.ReceiveAsync(customer.Id, 151m, manager.Id, "Cash"));
        await customerPayments.ReceiveAsync(customer.Id, 10m, manager.Id, "Card", "CUS-PAY-1");

        var reports = new ReportsService(db);
        var customerRows = await reports.GetCustomerLedgerAsync(admin.Id, today, today);
        var supplierRows = await reports.GetSupplierLedgerAsync(admin.Id, today, today);

        Assert.Equal(140m, customerRows[^1].Balance); // 120 opening + 100 sale - 20 payment - 50 return - 10 account payment
        Assert.Equal(60m, supplierRows[^1].Balance); // 30 opening + 80 purchase - 40 return - 10 payment
        Assert.Contains(customerRows, x => x.Type == "Balance brought forward" && x.Debit == 120);
        Assert.Contains(customerRows, x => x.Type == "Cash" && x.Credit == 20);
        Assert.Contains(customerRows, x => x.Type == "Account payment (Card)" && x.Credit == 10 && x.Reference == "CUS-PAY-1");
        Assert.Contains(supplierRows, x => x.Type == "Purchase return" && x.Debit == 40);
        Assert.Contains(supplierRows, x => x.Type == "Payment (Cash)" && x.Debit == 10 && x.Reference == "SUP-PAY-1");

        customer.OpeningBalance = 121;
        await Assert.ThrowsAsync<PosRuleException>(() => new CustomerService(db).SaveAsync(customer, admin.Id));
        customer.OpeningBalance = 120;
        supplier.OpeningBalance = 31;
        await Assert.ThrowsAsync<PosRuleException>(() => new SupplierService(db).SaveAsync(supplier, admin.Id));
        supplier.OpeningBalance = 30;
        Assert.Equal(120m, await db.Customers.AsNoTracking().Where(x => x.Id == customer.Id).Select(x => x.OpeningBalance).SingleAsync());
        Assert.Equal(30m, await db.Suppliers.AsNoTracking().Where(x => x.Id == supplier.Id).Select(x => x.OpeningBalance).SingleAsync());
    }
}
