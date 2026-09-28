using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PurchasePaymentTests
{
    [Fact]
    public async Task ConfiguredInvoiceNumberFormatIsUsedForSalesAndPurchases()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Stock", Sku = "INVOICE-FORMAT", PurchasePrice = 10, SalePrice = 20, CurrentStock = 2 };
        db.AddRange(admin, supplier, product);
        await db.SaveChangesAsync();
        await new SettingsService(db).SetAsync("InvoiceNumberFormat", "{PREFIX}_{SEQUENCE:D4}", admin.Id);

        var sale = await new SaleService(db).CompleteAsync([(product.Id, 1m)], admin.Id, null, 0m, 20m, "Cash", null);
        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 1m, 10m)]);

        Assert.Equal("INV_0001", sale.InvoiceNumber);
        Assert.Equal("PUR_0001", purchase.InvoiceNumber);
    }

    [Fact]
    public async Task PurchaseLineAndInvoiceTotalsAreRoundedToCurrencyPrecision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Fractional cost item", Sku = "ROUNDING", PurchasePrice = 1.005m, SalePrice = 2m };
        db.AddRange(admin, supplier, product);
        await db.SaveChangesAsync();

        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id,
            [new PurchaseLineInput(product.Id, 1m, 1.005m)]);

        Assert.Equal(1.01m, purchase.Items.Single().Total);
        Assert.Equal(1.01m, purchase.Total);
        Assert.Equal(1.01m, purchase.RemainingAmount);
    }

    [Fact]
    public async Task ReceivingPurchaseCanRecordInitialPaymentWithInvoiceAndSupplierLedgerAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test", Role = "Manager" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Stock", Sku = "INITIAL-PAY", PurchasePrice = 10, SalePrice = 20 };
        db.AddRange(admin, manager, supplier, product);
        await db.SaveChangesAsync();
        var service = new PurchaseService(db);
        var lines = new[] { new PurchaseLineInput(product.Id, 5m, 10m) };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReceiveWithPaymentAsync(supplier.Id, manager.Id, lines, 10m, "Cash"));
        await Assert.ThrowsAsync<PosRuleException>(() => service.ReceiveWithPaymentAsync(supplier.Id, admin.Id, lines, 51m, "Cash"));
        db.ChangeTracker.Clear();
        Assert.Equal(0m, await db.Products.Where(x => x.Id == product.Id).Select(x => x.CurrentStock).SingleAsync());
        Assert.Empty(await db.Purchases.ToListAsync());
        Assert.Empty(await db.SupplierPayments.ToListAsync());

        var purchase = await service.ReceiveWithPaymentAsync(supplier.Id, admin.Id, lines, 20m, "Bank transfer", "TX-START");
        Assert.Equal(50m, purchase.Total);
        Assert.Equal(20m, purchase.PaidAmount);
        Assert.Equal(30m, purchase.RemainingAmount);
        Assert.Equal("Partial", purchase.PaymentStatus);
        var payment = Assert.Single(await db.SupplierPayments.Where(x => x.PurchaseId == purchase.Id).ToListAsync());
        Assert.Equal(20m, payment.Amount);
        Assert.Equal("Bank transfer", payment.Method);
        Assert.Equal("TX-START", payment.Reference);
        Assert.Equal(5m, await db.Products.Where(x => x.Id == product.Id).Select(x => x.CurrentStock).SingleAsync());
    }

    [Fact]
    public async Task InvoicePaymentsTrackBalanceAndRejectOverpayment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test", Role = "Manager" };
        var supplier = new Supplier { Name = "Supplier" };
        var product = new Product { Name = "Stock item", Sku = "PAY-PUR", PurchasePrice = 10, SalePrice = 20, CurrentStock = 0 };
        db.AddRange(admin, manager, supplier, product);
        await db.SaveChangesAsync();

        var purchase = await new PurchaseService(db).ReceiveAsync(supplier.Id, admin.Id, [(product.Id, 5m, 10m)]);
        Assert.Equal(50m, purchase.Total);
        Assert.Equal(0m, purchase.PaidAmount);
        Assert.Equal(50m, purchase.RemainingAmount);
        Assert.Equal("Unpaid", purchase.PaymentStatus);

        var payments = new SupplierPaymentService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => payments.RecordForPurchaseAsync(purchase.Id, 10m, manager.Id, "Cash"));
        await payments.RecordForPurchaseAsync(purchase.Id, 20m, admin.Id, "Bank transfer", "TX-1");
        db.ChangeTracker.Clear();
        var partiallyPaid = await db.Purchases.SingleAsync(x => x.Id == purchase.Id);
        Assert.Equal(20m, partiallyPaid.PaidAmount);
        Assert.Equal(30m, partiallyPaid.RemainingAmount);
        Assert.Equal("Partial", partiallyPaid.PaymentStatus);

        await payments.RecordForPurchaseAsync(purchase.Id, 30m, admin.Id, "Cash");
        await Assert.ThrowsAsync<PosRuleException>(() => payments.RecordForPurchaseAsync(purchase.Id, 1m, admin.Id, "Cash"));
        db.ChangeTracker.Clear();
        var paid = await db.Purchases.SingleAsync(x => x.Id == purchase.Id);
        Assert.Equal(50m, paid.PaidAmount);
        Assert.Equal(0m, paid.RemainingAmount);
        Assert.Equal("Paid", paid.PaymentStatus);
        Assert.All(await db.SupplierPayments.Where(x => x.PurchaseId == purchase.Id).ToListAsync(), x => Assert.Equal(supplier.Id, x.SupplierId));
    }
}
