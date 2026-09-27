namespace SmartPOS.Core.Services;

public sealed record SaleLine(decimal Quantity, decimal UnitPrice, decimal UnitCost, decimal DiscountRate, decimal TaxRate);
public sealed record SaleTotals(decimal Subtotal, decimal Discount, decimal Tax, decimal GrandTotal, decimal Profit);

public static class SaleCalculator
{
    public static SaleTotals Calculate(IEnumerable<SaleLine> lines, decimal billDiscount = 0, decimal paid = 0)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || x.UnitCost < 0 || x.DiscountRate is < 0 or > 100 || x.TaxRate is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(lines), "Quantities must be positive and prices, discounts, and taxes must be valid.");
        if (billDiscount < 0 || paid < 0) throw new ArgumentOutOfRangeException(nameof(billDiscount));
        var subtotal = materialized.Sum(x => x.Quantity * x.UnitPrice);
        var lineDiscount = materialized.Sum(x => x.Quantity * x.UnitPrice * x.DiscountRate / 100m);
        var discount = lineDiscount + billDiscount;
        if (discount > subtotal) throw new ArgumentException("Discount cannot exceed the subtotal.", nameof(billDiscount));
        var tax = materialized.Sum(x => (x.Quantity * x.UnitPrice * (1 - x.DiscountRate / 100m)) * x.TaxRate / 100m);
        var total = decimal.Round(subtotal - discount + tax, 2, MidpointRounding.AwayFromZero);
        var cost = materialized.Sum(x => x.Quantity * x.UnitCost);
        return new(subtotal, discount, tax, total, decimal.Round(subtotal - discount - cost, 2, MidpointRounding.AwayFromZero));
    }
    public static decimal Change(decimal paid, decimal total) => Math.Max(0, paid - total);
}

public static class Permissions
{
    private static readonly IReadOnlyDictionary<string, string[]> Grants = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Administrator"] = ["*"],
        ["Manager"] = ["Sales.View", "Sales.Create", "Sales.Return", "Sales.Credit", "Payments.Receive", "Products.View", "Products.Manage", "Inventory.Manage", "Purchases.Manage", "Purchases.Return", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Reports.View", "Reports.Profit", "Settings.View", "Audit.View"],
        ["Cashier"] = ["Sales.View", "Sales.Create", "Customers.Manage", "Products.View"],
        ["Inventory Manager"] = ["Products.View", "Products.Manage", "Inventory.Manage", "Purchases.Manage", "Purchases.Return", "Suppliers.Manage"]
    };
    public static bool Has(string role, string permission) => Grants.TryGetValue(role, out var permissions) && (permissions.Contains("*") || permissions.Contains(permission, StringComparer.OrdinalIgnoreCase));
}
