using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class MixedPaymentTests
{
    [Fact]
    public async Task MixedCashAndCardMustExactlyCoverSaleAndAreRecordedSeparately()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var product = new Product { Name = "Notebook", Sku = "NB-1", CurrentStock = 2, PurchasePrice = 50, SalePrice = 100, TaxRate = 5 };
        db.AddRange(cashier, product);
        await db.SaveChangesAsync();
        var sales = new SaleService(db);

        await Assert.ThrowsAsync<PosRuleException>(() => sales.CompleteAsync([(product.Id, 1m)], cashier.Id, null, 0, 100, "Mixed", null,
            paymentBreakdown: [("Cash", 40m), ("Card", 60m)]));
        await Assert.ThrowsAsync<PosRuleException>(() => sales.CompleteAsync([(product.Id, 1m)], cashier.Id, null, 0, 105, "Cheque", null));
        await Assert.ThrowsAsync<PosRuleException>(() => sales.CompleteAsync([(product.Id, 1m)], cashier.Id, null, 0, 105, "Mixed", null,
            paymentBreakdown: [("Cheque", 105m)]));
        Assert.Equal(2m, product.CurrentStock);
        Assert.Empty(await db.Sales.ToListAsync());

        var sale = await sales.CompleteAsync([(product.Id, 1m)], cashier.Id, null, 0, 105, "Mixed", null,
            paymentBreakdown: [("cash", 40m), ("CARD", 65m)]);

        Assert.Equal("Mixed", sale.PaymentMethod);
        Assert.Equal(40m, sale.CashReceivedAmount);
        Assert.Equal(105m, sale.PaidAmount);
        Assert.Equal(2, sale.Payments.Count);
        Assert.Equal(40m, sale.Payments.Single(x => x.Method == "Cash").Amount);
        Assert.Equal(65m, sale.Payments.Single(x => x.Method == "Card").Amount);
        Assert.Equal(1m, product.CurrentStock);
    }
}
