using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleVoidTests
{
    [Fact]
    public async Task VoidingSaleReversesStockPaymentsAndReportsAndRequiresAdministrator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var manager = new AppUser { UserName = "manager", DisplayName = "Manager", PasswordHash = "test", Role = "Manager" };
        var customer = new Customer { Name = "Voided-sale customer" };
        var product = new Product { Name = "Voided item", Sku = "VOID-1", CurrentStock = 10, PurchasePrice = 5, SalePrice = 20 };
        db.AddRange(admin, manager, customer, product);
        await db.SaveChangesAsync();
        var sale = await new SaleService(db).CompleteAsync([(product.Id, 2m)], admin.Id, customer.Id, 0, 40, "Cash", null);
        Assert.Equal(8m, product.CurrentStock);

        var sales = new SaleService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.VoidAsync(sale.Id, manager.Id, "Customer changed mind"));
        Assert.Equal(8m, product.CurrentStock);
        await sales.VoidAsync(sale.Id, admin.Id, "Customer changed mind");
        await Assert.ThrowsAsync<PosRuleException>(() => sales.VoidAsync(sale.Id, admin.Id, "Duplicate void"));
        db.ChangeTracker.Clear();

        var voidedSale = await db.Sales.Include(x => x.Payments).SingleAsync(x => x.Id == sale.Id);
        Assert.Equal("Voided", voidedSale.Status);
        Assert.Equal(0m, voidedSale.RemainingAmount);
        Assert.Equal(0m, voidedSale.Payments.Sum(x => x.Amount));
        Assert.Equal(10m, await db.Products.Where(x => x.Id == product.Id).Select(x => x.CurrentStock).SingleAsync());
        Assert.Contains(await db.AuditEntries.ToListAsync(), x => x.Action == "Void" && x.Description.Contains("Customer changed mind"));
        Assert.Contains(await db.StockMovements.ToListAsync(), x => x.Reason == "SaleVoid" && x.QuantityDelta == 2m);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var reports = new ReportsService(db);
        Assert.Empty(await reports.GetDailySalesAsync(admin.Id, today, today));
        Assert.Empty(await reports.GetPaymentsAsync(admin.Id, today, today));
        Assert.Empty(await reports.GetCustomerLedgerAsync(admin.Id, today, today));
        await Assert.ThrowsAsync<PosRuleException>(() => new ReturnsService(db).ReturnAsync(sale.Id, admin.Id,
            [(sale.Items[0].Id, 1m)], "Attempt after void"));
    }

    [Fact]
    public async Task SaleWithAProcessedReturnCannotBeVoided()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var product = new Product { Name = "Returned item", Sku = "VOID-RETURN", CurrentStock = 2, PurchasePrice = 5, SalePrice = 20 };
        db.AddRange(admin, product);
        await db.SaveChangesAsync();
        var sale = await new SaleService(db).CompleteAsync([(product.Id, 1m)], admin.Id, null, 0, 20, "Cash", null);
        await new ReturnsService(db).ReturnAsync(sale.Id, admin.Id, [(sale.Items[0].Id, 1m)], "Already returned");

        await Assert.ThrowsAsync<PosRuleException>(() => new SaleService(db).VoidAsync(sale.Id, admin.Id, "Too late"));
        Assert.Equal(2m, product.CurrentStock);
        Assert.Equal("Returned", sale.Status);
    }
}
