using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class InvoiceSequenceConcurrencyTests
{
    [Fact]
    public async Task ConcurrentSaleCompletionsUseUniqueInvoiceNumbers()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"invoice-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connectionString).Options;
        int userId;
        int supplierId;
        int[] productIds;
        await using (var setup = new PosDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var admin = new AppUser { UserName = "invoice-admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
            setup.Users.Add(admin);
            var supplier = new Supplier { Name = "Concurrent invoice supplier" };
            setup.Suppliers.Add(supplier);
            for (var i = 0; i < 8; i++)
                setup.Products.Add(new Product { Name = $"Item {i}", Sku = $"INV-CONC-{i}", CurrentStock = 2, PurchasePrice = 1, SalePrice = 10 });
            await setup.SaveChangesAsync();
            userId = admin.Id;
            supplierId = supplier.Id;
            productIds = await setup.Products.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        }

        var attempts = productIds.Select(async productId =>
        {
            await using var db = new PosDbContext(options);
            return await new SaleService(db).CompleteAsync([(productId, 1m)], userId, null, 0, 10, "Cash", null);
        });

        var sales = await Task.WhenAll(attempts);

        Assert.Equal(productIds.Length, sales.Select(x => x.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(productIds.Length, sales.Select(x => x.InvoiceNumber).Select(x => x.Split('-').Last()).Distinct().Count());

        var purchases = await Task.WhenAll(productIds.Select(async productId =>
        {
            await using var db = new PosDbContext(options);
            return await new PurchaseService(db).ReceiveAsync(supplierId, userId, [(productId, 1m, 1m)]);
        }));
        Assert.Equal(productIds.Length, purchases.Select(x => x.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());
    }
}
