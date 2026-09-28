using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ServiceInputValidationTests
{
    [Fact]
    public async Task ServicesRejectDecimalPrecisionBeyondThePersistedFourPlaceScale()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "precision-admin", DisplayName = "Admin", PasswordHash = "hash", Role = "Administrator" };
        var supplier = new Supplier { Name = "Precision supplier" };
        var customer = new Customer { Name = "Precision customer", CreditLimit = 100 };
        var product = new Product { Name = "Precision item", Sku = "PRECISION-1", CurrentStock = 5, PurchasePrice = 1, SalePrice = 10 };
        db.AddRange(admin, supplier, customer, product);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PosRuleException>(() => new ProductService(db).SaveAsync(new Product
        {
            Name = "Too precise", Sku = "PRECISION-2", PurchasePrice = 1, SalePrice = 10.00001m
        }, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => new SaleService(db).CompleteAsync(
            [(product.Id, 1.00001m)], admin.Id, null, 0, 10, "Cash", null));
        await Assert.ThrowsAsync<PosRuleException>(() => new PurchaseService(db).ReceiveAsync(
            supplier.Id, admin.Id, [(product.Id, 1.00001m, 1m)]));
        await Assert.ThrowsAsync<PosRuleException>(() => new InventoryService(db).AdjustAsync(
            product.Id, 0.00001m, admin.Id, "Precision check"));
        await Assert.ThrowsAsync<PosRuleException>(() => new ExpenseService(db).RecordAsync(
            "Precision check", "General", 1.00001m, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => new CustomerService(db).SaveAsync(new Customer
        {
            Name = "Too precise balance", OpeningBalance = 1.00001m
        }, admin.Id));

        Assert.Equal(5m, await db.Products.Where(x => x.Id == product.Id).Select(x => x.CurrentStock).SingleAsync());
        Assert.Empty(await db.Sales.ToListAsync());
        Assert.Empty(await db.Purchases.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.Expenses.ToListAsync());
        Assert.Equal(1, await db.Customers.CountAsync());
        Assert.Equal(0m, await db.Customers.Where(x => x.Id == customer.Id).Select(x => x.OpeningBalance).SingleAsync());
    }

    [Fact]
    public async Task BackOfficeAndCatalogueServicesRejectOversizedTextBeforePersistence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var admin = new AppUser { UserName = "validation-admin", DisplayName = "Admin", PasswordHash = "hash", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var customers = new CustomerService(db);
        var suppliers = new SupplierService(db);
        var expenses = new ExpenseService(db);
        var users = new UserManagementService(db, new PasswordHasher());
        var taxonomy = new CatalogTaxonomyService(db);
        var products = new ProductService(db);

        await Assert.ThrowsAsync<PosRuleException>(() => customers.SaveAsync(new Customer { Name = new string('C', 201) }, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => suppliers.SaveAsync(new Supplier { Name = "Supplier", Email = new string('e', 255) }, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => suppliers.SaveAsync(new Supplier { Name = "Supplier", Company = new string('c', 201) }, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => expenses.RecordAsync(new string('E', 251), "General", 10, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => users.CreateAsync(new string('u', 65), "User", "a sufficiently long password", "Cashier", admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => taxonomy.SaveCategoryAsync(new Category { Name = new string('K', 101) }, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => products.SaveAsync(new Product { Name = "Product", Sku = new string('S', 65) }, admin.Id));

        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.Suppliers.ToListAsync());
        Assert.Empty(await db.Expenses.ToListAsync());
        Assert.Single(await db.Users.ToListAsync());
        Assert.Empty(await db.Categories.ToListAsync());
        Assert.Empty(await db.Products.ToListAsync());

        var savedSupplier = await suppliers.SaveAsync(new Supplier { Name = "ABC Traders", Company = "ABC Trading Company", Phone = "03001234567", Address = "Market Road" }, admin.Id);
        db.ChangeTracker.Clear();
        var persistedSupplier = await db.Suppliers.SingleAsync(x => x.Id == savedSupplier.Id);
        Assert.Equal("ABC Trading Company", persistedSupplier.Company);
        Assert.Equal(savedSupplier.Id, Assert.Single(await suppliers.SearchAsync("Trading", admin.Id)).Id);
    }
}
