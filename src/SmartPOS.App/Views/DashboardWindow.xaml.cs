using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardService _dashboard;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppUser _user;
    private readonly DashboardViewModel _viewModel;
    public bool IsSigningOut { get; private set; }
    public DashboardWindow(DashboardService dashboard, AppUser user, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent(); _dashboard = dashboard; _user = user; _scopeFactory = scopeFactory;
        UserInfo.Text = $"{user.DisplayName} · {user.Role}"; _viewModel = new DashboardViewModel(dashboard, user.Id, user.Role); DataContext = _viewModel;
        PosNavButton.Visibility = Permissions.Has(_user.Role, "Sales.Create") ? Visibility.Visible : Visibility.Collapsed;
        ProductsNavButton.Visibility = Permissions.Has(_user.Role, "Products.Manage") ? Visibility.Visible : Visibility.Collapsed;
        PurchasesNavButton.Visibility = Permissions.Has(_user.Role, "Purchases.Manage") || Permissions.Has(_user.Role, "Purchases.Return") ? Visibility.Visible : Visibility.Collapsed;
        SalesNavButton.Visibility = Permissions.Has(_user.Role, "Sales.View") ? Visibility.Visible : Visibility.Collapsed;
        ReportsNavButton.Visibility = Permissions.Has(_user.Role, "Reports.View") ? Visibility.Visible : Visibility.Collapsed;
        OperationsNavButton.Visibility = new[] { "Customers.View", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Payments.Receive", "Payments.Make", "Settings.View", "Database.Backup", "Database.Restore", "Users.Manage", "Audit.View" }
            .Any(permission => Permissions.Has(_user.Role, permission)) ? Visibility.Visible : Visibility.Collapsed;
        var canViewSales = Permissions.Has(_user.Role, "Sales.View");
        var canViewPurchases = Permissions.Has(_user.Role, "Purchases.Manage") || Permissions.Has(_user.Role, "Purchases.Return");
        var canViewExpenses = Permissions.Has(_user.Role, "Expenses.Manage") || Permissions.Has(_user.Role, "Reports.View");
        var canViewProfit = Permissions.Has(_user.Role, "Reports.Profit");
        SalesMetricCard.Visibility = SalesChartCard.Visibility = canViewSales ? Visibility.Visible : Visibility.Collapsed;
        PurchasesMetricCard.Visibility = PurchasesChartCard.Visibility = canViewPurchases ? Visibility.Visible : Visibility.Collapsed;
        ExpensesMetricCard.Visibility = canViewExpenses ? Visibility.Visible : Visibility.Collapsed;
        ProfitMetricCard.Visibility = canViewProfit ? Visibility.Visible : Visibility.Collapsed;
        RecentSalesCard.Visibility = Permissions.Has(_user.Role, "Sales.View") ? Visibility.Visible : Visibility.Collapsed;
        RecentPurchasesCard.Visibility = Permissions.Has(_user.Role, "Purchases.Manage") || Permissions.Has(_user.Role, "Purchases.Return") ? Visibility.Visible : Visibility.Collapsed;
        if (RecentSalesCard.Visibility == Visibility.Collapsed) RecentActivityGrid.ColumnDefinitions[1].Width = new GridLength(0);
        if (RecentPurchasesCard.Visibility == Visibility.Collapsed) RecentActivityGrid.ColumnDefinitions[2].Width = new GridLength(0);
        ProfitChartCard.Visibility = canViewProfit ? Visibility.Visible : Visibility.Collapsed;
        if (!canViewSales) ChartsGrid.ColumnDefinitions[0].Width = new GridLength(0);
        if (!canViewPurchases) ChartsGrid.ColumnDefinitions[1].Width = new GridLength(0);
        if (!canViewProfit) ChartsGrid.ColumnDefinitions[2].Width = new GridLength(0);
        Loaded += async (_, _) => await _viewModel.RefreshAsync(canViewProfit);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    private async void Operations_Click(object sender, RoutedEventArgs e)
    {
        if (!new[] { "Customers.View", "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Payments.Receive" }.Any(p => Permissions.Has(_user.Role, p)))
        { MessageBox.Show("Your role cannot access business operations.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
        {
            var services = scope.ServiceProvider;
            new BackOfficeWindow(_user,
                services.GetRequiredService<CustomerService>(), services.GetRequiredService<SupplierService>(),
                services.GetRequiredService<ExpenseService>(), services.GetRequiredService<PaymentService>(),
                services.GetRequiredService<CustomerAccountPaymentService>(), services.GetRequiredService<SupplierPaymentService>(),
                services.GetRequiredService<BackOfficeQueryService>(), services.GetRequiredService<SettingsService>(),
                services.GetRequiredService<DatabaseBackupService>(), services.GetRequiredService<AuditService>(),
                services.GetRequiredService<UserManagementService>()) { Owner = this }.ShowDialog();
        }
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Sales_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Sales.View")) { MessageBox.Show("Your role cannot view sales.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
            new SalesHistoryWindow(scope.ServiceProvider.GetRequiredService<SaleService>(),
                scope.ServiceProvider.GetRequiredService<ReturnsService>(), _user,
                scope.ServiceProvider.GetRequiredService<ReceiptPrinter>()) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Purchase_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Purchases.Manage") && !Permissions.Has(_user.Role, "Purchases.Return")) { MessageBox.Show("Your role cannot access purchases.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
        {
            var services = scope.ServiceProvider;
            new PurchaseWindow(services.GetRequiredService<ProductService>(), services.GetRequiredService<SupplierService>(),
                services.GetRequiredService<PurchaseService>(), services.GetRequiredService<PurchaseReturnsService>(),
                services.GetRequiredService<SupplierPaymentService>(), _user) { Owner = this }.ShowDialog();
        }
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Reports_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Reports.View")) { MessageBox.Show("Your role cannot view reports.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
            new ReportsWindow(scope.ServiceProvider.GetRequiredService<ReportsService>(), _user) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Products_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Products.Manage")) { MessageBox.Show("Your role cannot manage products.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
        {
            var services = scope.ServiceProvider;
            new ProductsWindow(services.GetRequiredService<ProductService>(), services.GetRequiredService<InventoryService>(),
                services.GetRequiredService<CatalogTaxonomyService>(), _user) { Owner = this }.ShowDialog();
        }
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Pos_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Sales.Create")) { MessageBox.Show("Your role cannot create sales.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
        {
            var services = scope.ServiceProvider;
            new PosWindow(services.GetRequiredService<SaleService>(), services.GetRequiredService<HeldSaleService>(),
                services.GetRequiredService<ProductService>(), services.GetRequiredService<InventoryService>(),
                services.GetRequiredService<CatalogTaxonomyService>(), services.GetRequiredService<PosScreenService>(), _user,
                services.GetRequiredService<ReceiptPrinter>()) { Owner = this }.ShowDialog();
        }
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Sign out and close {SmartPosIdentity.Name}?", "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            IsSigningOut = true;
            Close();
        }
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (e.Key == Key.F1) { Pos_Click(this, new RoutedEventArgs()); e.Handled = true; } base.OnPreviewKeyDown(e); }
}
