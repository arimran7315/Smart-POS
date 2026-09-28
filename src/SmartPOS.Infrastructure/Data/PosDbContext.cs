using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Data;

public sealed class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>(); public DbSet<Category> Categories => Set<Category>(); public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Customer> Customers => Set<Customer>(); public DbSet<Supplier> Suppliers => Set<Supplier>(); public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Sale> Sales => Set<Sale>(); public DbSet<SaleItem> SaleItems => Set<SaleItem>(); public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>(); public DbSet<Payment> Payments => Set<Payment>(); public DbSet<SaleReturn> Returns => Set<SaleReturn>(); public DbSet<SaleReturnItem> ReturnItems => Set<SaleReturnItem>(); public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>(); public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>(); public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>(); public DbSet<CustomerAccountPayment> CustomerAccountPayments => Set<CustomerAccountPayment>();
    public DbSet<HeldSale> HeldSales => Set<HeldSale>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>(); public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>(); public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>().HasIndex(x => x.Sku).IsUnique(); b.Entity<Product>().HasIndex(x => x.Barcode).IsUnique().HasFilter("Barcode IS NOT NULL");
        b.Entity<Product>().HasIndex(x => x.Name);
        b.Entity<Product>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Category>().HasIndex(x => x.Name).IsUnique(); b.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
        b.Entity<AppUser>().HasIndex(x => x.UserName).IsUnique(); b.Entity<Sale>().HasIndex(x => x.InvoiceNumber).IsUnique();
        b.Entity<Sale>().HasIndex(x => x.SoldAtUtc);
        b.Entity<Purchase>().HasIndex(x => x.InvoiceNumber).IsUnique(); b.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Purchase>().HasIndex(x => x.PurchasedAtUtc);
        b.Entity<Product>().Property(x => x.StockVersion).IsConcurrencyToken();
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(type.ClrType).Property<DateTime>(nameof(Entity.CreatedAtUtc)).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            foreach (var p in type.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                var property = b.Entity(type.ClrType).Property(p.Name);
                if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" && p.ClrType == typeof(decimal))
                    property.HasConversion(new ValueConverter<decimal, long>(v => (long)decimal.Round(v * 10_000m, 0, MidpointRounding.AwayFromZero), v => v / 10_000m)).HasColumnType("INTEGER");
                else property.HasPrecision(18, 4);
            }
        }
        b.Entity<Sale>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Purchase>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Purchase>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Purchase>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturn>().HasIndex(x => x.ReturnNumber).IsUnique();
        b.Entity<SupplierPayment>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupplierPayment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SupplierPayment>().HasOne<Purchase>().WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CustomerAccountPayment>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CustomerAccountPayment>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturn>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturn>().HasOne<Purchase>().WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturn>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturnItem>().HasOne<PurchaseItem>().WithMany().HasForeignKey(x => x.PurchaseItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PurchaseReturnItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HeldSale>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HeldSale>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<SaleReturn>().HasIndex(x => x.ReturnNumber).IsUnique();
        b.Entity<SaleReturn>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleReturnId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleReturn>().HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleReturn>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleReturnItem>().HasOne<SaleItem>().WithMany().HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleReturnItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Expense>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEntry>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Product>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
    }
}
