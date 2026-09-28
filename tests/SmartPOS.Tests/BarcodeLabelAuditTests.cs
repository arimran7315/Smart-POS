using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class BarcodeLabelAuditTests
{
    [Fact]
    public async Task RecordingLabelPrintRequiresProductManagementAndAuditsTheSavedProduct()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "hash", Role = "Administrator" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "hash", Role = "Cashier" };
        var product = new Product { Name = "Coffee", Sku = "CF-100", Barcode = "123456789012", SalePrice = 4.5m };
        db.AddRange(admin, cashier, product);
        await db.SaveChangesAsync();

        var service = new ProductService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordBarcodeLabelsPrintedAsync(product.Id, 3, cashier.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => service.RecordBarcodeLabelsPrintedAsync(product.Id, 501, admin.Id));
        Assert.Empty(await db.AuditEntries.ToListAsync());

        await service.RecordBarcodeLabelsPrintedAsync(product.Id, 3, admin.Id);
        var audit = await db.AuditEntries.SingleAsync();
        Assert.Equal(admin.Id, audit.UserId);
        Assert.Equal("Print", audit.Action);
        Assert.Equal("Product", audit.EntityName);
        Assert.Equal(product.Id.ToString(), audit.EntityId);
        Assert.Contains("3 barcode label(s)", audit.Description);
        Assert.Contains(product.Sku, audit.Description);
    }
}
