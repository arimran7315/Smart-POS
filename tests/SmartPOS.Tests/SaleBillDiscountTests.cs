using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleBillDiscountTests
{
    [Fact]
    public async Task FinalPartialReturnRefundSettlesAnyRoundingRemainder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "return-admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var product = new Product { Name = "Fractional return", Sku = "RET-ROUND", CurrentStock = 3, PurchasePrice = 1, SalePrice = 3.33m, TaxRate = 10 };
        db.AddRange(admin, product);
        await db.SaveChangesAsync();

        var sale = await new SaleService(db).CompleteAsync([(product.Id, 3m)], admin.Id, null, 0, 10.99m, "Cash", null);
        var item = Assert.Single(sale.Items);
        Assert.Equal(10.99m, item.Total);

        var returns = new ReturnsService(db);
        var first = await returns.ReturnAsync(sale.Id, admin.Id, [(item.Id, 1m)], "Partial return");
        var second = await returns.ReturnAsync(sale.Id, admin.Id, [(item.Id, 1m)], "Partial return");
        var final = await returns.ReturnAsync(sale.Id, admin.Id, [(item.Id, 1m)], "Final return");

        Assert.Equal(3.66m, first.Total);
        Assert.Equal(3.66m, second.Total);
        Assert.Equal(3.67m, final.Total);
        Assert.Equal(item.Total, first.Total + second.Total + final.Total);
        Assert.Equal(0m, sale.PaidAmount);
        Assert.Equal("Returned", sale.Status);
    }

    [Fact]
    public async Task BillDiscountIsAllocatedToLinesAndPartialReturnsUseDiscountedTaxAndTotals()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var lowTax = new Product { Name = "Low tax", Sku = "DISC-LOW", CurrentStock = 2, PurchasePrice = 40, SalePrice = 100, TaxRate = 10 };
        var highTax = new Product { Name = "High tax", Sku = "DISC-HIGH", CurrentStock = 2, PurchasePrice = 60, SalePrice = 100, TaxRate = 20 };
        db.AddRange(admin, lowTax, highTax);
        await db.SaveChangesAsync();

        var sale = await new SaleService(db).CompleteAsync([(lowTax.Id, 1m), (highTax.Id, 1m)], admin.Id,
            null, 10m, 218.50m, "Cash", null);
        Assert.Equal(10m, sale.Discount);
        Assert.Equal(28.50m, sale.Tax);
        Assert.Equal(218.50m, sale.GrandTotal);
        Assert.Equal(sale.GrandTotal, sale.Items.Sum(x => x.Total));
        Assert.Equal(sale.Discount, sale.Items.Sum(x => x.Discount));
        Assert.Equal(sale.Tax, sale.Items.Sum(x => x.Tax));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var profitReport = new ReportsService(db);
        var saleProfit = Assert.Single(await profitReport.GetDailyProfitAsync(admin.Id, today, today)).Profit;
        Assert.Equal(90m, saleProfit);
        Assert.Equal(saleProfit, (await new DashboardService(db).GetTodayAsync(admin.Id)).TodayProfit);

        var lowTaxItem = sale.Items.Single(x => x.ProductId == lowTax.Id);
        var returned = await new ReturnsService(db).ReturnAsync(sale.Id, admin.Id, [(lowTaxItem.Id, 1m)], "Discounted sale return");
        Assert.Equal(104.50m, returned.Total);
        var netProfit = Assert.Single(await profitReport.GetDailyProfitAsync(admin.Id, today, today)).Profit;
        Assert.Equal(35m, netProfit);
        Assert.Equal(netProfit, (await new DashboardService(db).GetTodayAsync(admin.Id)).TodayProfit);
        var taxReport = await new ReportsService(db).GetTaxByDayAsync(admin.Id, today, today);
        Assert.Equal(19m, Assert.Single(taxReport).NetTax);
    }
}
