using SmartPOS.Core.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class SaleCalculatorTests
{
    [Fact]
    public void Calculate_AppliesLineDiscountAndTaxAndProfit()
    {
        var totals = SaleCalculator.Calculate([new SaleLine(2, 100, 60, 10, 5)]);
        Assert.Equal(200m, totals.Subtotal); Assert.Equal(20m, totals.Discount); Assert.Equal(9m, totals.Tax);
        Assert.Equal(189m, totals.GrandTotal); Assert.Equal(60m, totals.Profit);
    }
    [Fact]
    public void Calculate_AppliesBillDiscountAndRejectsExcessDiscount()
    {
        Assert.Equal(95m, SaleCalculator.Calculate([new SaleLine(1, 100, 40, 0, 0)], billDiscount: 5).GrandTotal);
        Assert.Throws<ArgumentException>(() => SaleCalculator.Calculate([new SaleLine(1, 100, 40, 0, 0)], billDiscount: 101));
    }
    [Fact]
    public void Change_NeverReturnsNegative()
    {
        Assert.Equal(3m, SaleCalculator.Change(13m, 10m)); Assert.Equal(0m, SaleCalculator.Change(8m, 10m));
    }
    [Fact]
    public void Permissions_RestrictProfitReportsToPrivilegedRoles()
    {
        Assert.True(Permissions.Has("Administrator", "Reports.Profit")); Assert.True(Permissions.Has("Manager", "Reports.Profit"));
        Assert.False(Permissions.Has("Cashier", "Reports.Profit"));
    }
}
