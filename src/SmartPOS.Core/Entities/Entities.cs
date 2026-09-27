namespace SmartPOS.Core.Entities;

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class Category : Entity { public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }
public sealed class Brand : Entity { public string Name { get; set; } = ""; public bool IsActive { get; set; } = true; }
public sealed class Product : Entity
{
    public string Name { get; set; } = ""; public string Sku { get; set; } = ""; public string? Barcode { get; set; }
    public int? CategoryId { get; set; } public Category? Category { get; set; } public int? BrandId { get; set; } public Brand? Brand { get; set; }
    public string Unit { get; set; } = "each"; public decimal PurchasePrice { get; set; } public decimal SalePrice { get; set; }
    public decimal WholesalePrice { get; set; } public decimal TaxRate { get; set; } public decimal DiscountRate { get; set; }
    public decimal CurrentStock { get; set; } public decimal MinimumStock { get; set; } public decimal MaximumStock { get; set; }
    public string? ImagePath { get; set; } public string? Description { get; set; } public bool IsActive { get; set; } = true;
}
public sealed class Customer : Entity { public string Name { get; set; } = ""; public string? Phone { get; set; } public string? Email { get; set; } public decimal CreditLimit { get; set; } public bool IsActive { get; set; } = true; }
public sealed class Supplier : Entity { public string Name { get; set; } = ""; public string? Phone { get; set; } public string? Email { get; set; } public string? Address { get; set; } public bool IsActive { get; set; } = true; }
public sealed class AppUser : Entity { public string UserName { get; set; } = ""; public string DisplayName { get; set; } = ""; public string PasswordHash { get; set; } = ""; public string Role { get; set; } = "Cashier"; public bool IsActive { get; set; } = true; }
public sealed class Sale : Entity
{
    public string InvoiceNumber { get; set; } = ""; public int? CustomerId { get; set; } public int UserId { get; set; }
    public DateTime SoldAtUtc { get; set; } = DateTime.UtcNow; public decimal Subtotal { get; set; } public decimal Discount { get; set; }
    public decimal Tax { get; set; } public decimal GrandTotal { get; set; } public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; } public decimal CashReceivedAmount { get; set; } public string PaymentMethod { get; set; } = "Cash"; public string Status { get; set; } = "Completed";
    public string? Notes { get; set; } public List<SaleItem> Items { get; set; } = []; public List<Payment> Payments { get; set; } = [];
}
public sealed class SaleItem : Entity { public int SaleId { get; set; } public int ProductId { get; set; } public string ProductName { get; set; } = ""; public decimal Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal UnitCost { get; set; } public decimal Discount { get; set; } public decimal Tax { get; set; } public decimal Total { get; set; } }
public sealed class Purchase : Entity { public string InvoiceNumber { get; set; } = ""; public int SupplierId { get; set; } public int UserId { get; set; } public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow; public decimal Total { get; set; } public string Status { get; set; } = "Received"; public List<PurchaseItem> Items { get; set; } = []; }
public sealed class PurchaseItem : Entity { public int PurchaseId { get; set; } public int ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } public decimal Total { get; set; } }
public sealed class PurchaseReturn : Entity { public string ReturnNumber { get; set; } = ""; public int PurchaseId { get; set; } public int UserId { get; set; } public DateTime ReturnedAtUtc { get; set; } = DateTime.UtcNow; public decimal Total { get; set; } public string Reason { get; set; } = ""; public List<PurchaseReturnItem> Items { get; set; } = []; }
public sealed class PurchaseReturnItem : Entity { public int PurchaseReturnId { get; set; } public int PurchaseItemId { get; set; } public int ProductId { get; set; } public decimal Quantity { get; set; } public decimal Total { get; set; } }
public sealed class Payment : Entity { public int SaleId { get; set; } public decimal Amount { get; set; } public string Method { get; set; } = "Cash"; public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow; public string? Reference { get; set; } }
public sealed class SaleReturn : Entity { public string ReturnNumber { get; set; } = ""; public int SaleId { get; set; } public int UserId { get; set; } public DateTime ReturnedAtUtc { get; set; } = DateTime.UtcNow; public decimal Total { get; set; } public string Reason { get; set; } = ""; public List<SaleReturnItem> Items { get; set; } = []; }
public sealed class SaleReturnItem : Entity { public int SaleReturnId { get; set; } public int SaleItemId { get; set; } public int ProductId { get; set; } public decimal Quantity { get; set; } public decimal Refund { get; set; } }
public sealed class Expense : Entity { public string Description { get; set; } = ""; public string Category { get; set; } = "General"; public decimal Amount { get; set; } public DateTime SpentAtUtc { get; set; } = DateTime.UtcNow; public int UserId { get; set; } }
public sealed class StockMovement : Entity { public int ProductId { get; set; } public decimal QuantityDelta { get; set; } public decimal BalanceAfter { get; set; } public string Reason { get; set; } = "Adjustment"; public string? Reference { get; set; } public int UserId { get; set; } }
public sealed class AuditEntry : Entity { public int? UserId { get; set; } public string Action { get; set; } = ""; public string EntityName { get; set; } = ""; public string? EntityId { get; set; } public string Description { get; set; } = ""; }
public sealed class AppSetting : Entity { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
