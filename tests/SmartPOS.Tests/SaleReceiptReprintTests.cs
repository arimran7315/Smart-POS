using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleReceiptReprintTests
{
    [Fact]
    public async Task ReprintDataUsesOriginalCashierAndCustomerAndEnforcesSaleVisibility()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Original cashier", PasswordHash = "hash", Role = "Cashier" };
        var otherCashier = new AppUser { UserName = "other", DisplayName = "Other cashier", PasswordHash = "hash", Role = "Cashier" };
        var customer = new Customer { Name = "Receipt customer" };
        var product = new Product { Name = "Coffee", Sku = "RCP-1", SalePrice = 5m, CurrentStock = 1 };
        var sale = new Sale
        {
            InvoiceNumber = "S-100", UserId = 0, CustomerId = 0, Status = "Completed", PaymentMethod = "Cash",
            Subtotal = 5m, GrandTotal = 5m, PaidAmount = 5m, CashReceivedAmount = 5m
        };
        var voided = new Sale { InvoiceNumber = "S-101", Status = "Voided", PaymentMethod = "Cash" };
        db.AddRange(cashier, otherCashier, customer, product);
        await db.SaveChangesAsync();
        sale.UserId = cashier.Id;
        sale.CustomerId = customer.Id;
        voided.UserId = cashier.Id;
        db.AddRange(sale, voided);
        await db.SaveChangesAsync();
        db.SaleItems.Add(new SaleItem { SaleId = sale.Id, ProductId = product.Id, ProductName = product.Name, Quantity = 1m, UnitPrice = 5m, Total = 5m });
        db.Settings.Add(new AppSetting { Key = "StoreName", Value = "Coffee Shop" });
        await db.SaveChangesAsync();

        var sales = new SaleService(db);
        var receipt = await sales.GetReceiptForReprintAsync(sale.Id, cashier.Id);
        Assert.Equal("Original cashier", receipt.CashierName);
        Assert.Equal("Receipt customer", receipt.CustomerName);
        Assert.Equal("Coffee Shop", receipt.Settings["StoreName"]);
        Assert.Single(receipt.Sale.Items);

        await Assert.ThrowsAsync<PosRuleException>(() => sales.GetReceiptForReprintAsync(sale.Id, otherCashier.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => sales.GetReceiptForReprintAsync(voided.Id, cashier.Id));
        await sales.RecordReceiptPrintedAsync(sale.Id, cashier.Id);
        await sales.RecordReceiptReprintedAsync(sale.Id, cashier.Id);
        var audits = await db.AuditEntries.OrderBy(x => x.Id).ToListAsync();
        Assert.Collection(audits,
            printed => { Assert.Equal("Print", printed.Action); Assert.Equal("Sale", printed.EntityName); Assert.Equal(sale.InvoiceNumber, printed.EntityId); Assert.Equal(cashier.Id, printed.UserId); Assert.Contains("Printed receipt", printed.Description); },
            reprinted => { Assert.Equal("Print", reprinted.Action); Assert.Equal("Sale", reprinted.EntityName); Assert.Equal(sale.InvoiceNumber, reprinted.EntityId); Assert.Equal(cashier.Id, reprinted.UserId); Assert.Contains("Reprinted receipt", reprinted.Description); });
    }
}
