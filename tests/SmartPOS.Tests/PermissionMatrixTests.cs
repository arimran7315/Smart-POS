using SmartPOS.Core.Services;
using SmartPOS.App.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class PermissionMatrixTests
{
    [Fact]
    public void AdministratorHasEveryApplicationPermission()
    {
        foreach (var permission in KnownPermissions)
            Assert.True(Permissions.Has("Administrator", permission), $"Administrator should have {permission}.");
    }

    [Fact]
    public void ManagerCanRunBusinessOperationsButCannotUseAdministratorOnlyControls()
    {
        AssertGranted("Manager", "Sales.View", "Sales.ViewAll", "Sales.Create", "Sales.Return", "Sales.Credit",
            "Payments.Receive", "Products.View", "Products.Manage", "Inventory.Manage", "Purchases.Manage",
            "Purchases.Return", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Reports.View",
            "Reports.Profit", "Settings.View", "Audit.View");
        AssertDenied("Manager", "Sales.Void", "Sales.Discount", "Payments.Make", "Settings.Manage", "Users.Manage",
            "Database.Backup", "Database.Restore");
    }

    [Fact]
    public void CashierCanSellAndLookUpRecordsButCannotUseBackOfficeOrSensitiveReports()
    {
        AssertGranted("Cashier", "Sales.View", "Sales.Create", "Customers.View", "Products.View");
        AssertDenied("Cashier", "Sales.ViewAll", "Sales.Return", "Sales.Credit", "Sales.Void", "Sales.Discount",
            "Products.Manage", "Purchases.Manage", "Purchases.Return", "Inventory.Manage", "Customers.Manage",
            "Suppliers.Manage", "Expenses.Manage", "Payments.Receive", "Payments.Make", "Reports.View",
            "Reports.Profit", "Settings.View", "Settings.Manage", "Users.Manage", "Audit.View",
            "Database.Backup", "Database.Restore");
    }

    [Fact]
    public void InventoryManagerCanManageStockAndPurchasesButCannotCreateSalesOrViewFinancialReports()
    {
        AssertGranted("Inventory Manager", "Products.View", "Products.Manage", "Inventory.Manage",
            "Purchases.Manage", "Purchases.Return", "Suppliers.Manage");
        AssertDenied("Inventory Manager", "Sales.View", "Sales.Create", "Sales.Return", "Payments.Receive",
            "Payments.Make", "Customers.View", "Customers.Manage", "Expenses.Manage", "Reports.View",
            "Reports.Profit", "Settings.View", "Users.Manage", "Audit.View", "Database.Backup", "Database.Restore");
    }

    [Theory]
    [InlineData("Cashier")]
    [InlineData("Manager")]
    [InlineData("Inventory Manager")]
    public void RoleAndPermissionNamesAreCaseInsensitive(string role)
    {
        Assert.True(Permissions.Has(role.ToUpperInvariant(), "products.view"));
    }

    [Fact]
    public void UnknownRoleAndPermissionAreDenied()
    {
        Assert.False(Permissions.Has("Unknown role", "Sales.Create"));
        Assert.False(Permissions.Has("Cashier", "Unknown.Permission"));
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Manager", true)]
    [InlineData("Cashier", true)]
    [InlineData("Inventory Manager", true)]
    [InlineData("Unknown role", false)]
    public void OperationsNavigationAndClickGuardShareRoleAccess(string role, bool expected) =>
        Assert.Equal(expected, DashboardAccessPolicy.CanOpenOperations(role));

    private static readonly string[] KnownPermissions =
    [
        "Sales.View", "Sales.ViewAll", "Sales.Create", "Sales.Return", "Sales.Credit", "Sales.Void", "Sales.Discount",
        "Payments.Receive", "Payments.Make", "Products.View", "Products.Manage", "Inventory.Manage", "Purchases.Manage",
        "Purchases.Return", "Customers.View", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Reports.View",
        "Reports.Profit", "Settings.View", "Settings.Manage", "Users.Manage", "Audit.View", "Database.Backup", "Database.Restore"
    ];

    private static void AssertGranted(string role, params string[] permissions)
    {
        foreach (var permission in permissions)
            Assert.True(Permissions.Has(role, permission), $"{role} should have {permission}.");
    }

    private static void AssertDenied(string role, params string[] permissions)
    {
        foreach (var permission in permissions)
            Assert.False(Permissions.Has(role, permission), $"{role} should not have {permission}.");
    }
}
