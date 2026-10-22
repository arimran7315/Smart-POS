using SmartPOS.Core.Services;

namespace SmartPOS.App.Services;

public static class DashboardAccessPolicy
{
    private static readonly string[] OperationsPermissions =
    [
        "Customers.View", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage",
        "Payments.Receive", "Payments.Make", "Settings.View", "Database.Backup",
        "Database.Restore", "Users.Manage", "Audit.View"
    ];

    public static bool CanOpenOperations(string role) =>
        OperationsPermissions.Any(permission => Permissions.Has(role, permission));
}
