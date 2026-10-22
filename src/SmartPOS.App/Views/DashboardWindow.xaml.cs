using System.Windows;
using System.Windows.Controls;
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
        InitializeComponent();
        WindowLayout.FitToWorkArea(this);
        _dashboard = dashboard; _user = user; _scopeFactory = scopeFactory;
        DashboardContent.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
                ApplyResponsiveLayout(e.NewSize.Width);
        };
        UserInfo.Text = $"{user.DisplayName} · {user.Role}"; _viewModel = new DashboardViewModel(dashboard, user.Id, user.Role); DataContext = _viewModel;
        PosNavButton.Visibility = Permissions.Has(_user.Role, "Sales.Create") ? Visibility.Visible : Visibility.Collapsed;
        ProductsNavButton.Visibility = Permissions.Has(_user.Role, "Products.Manage") ? Visibility.Visible : Visibility.Collapsed;
        PurchasesNavButton.Visibility = Permissions.Has(_user.Role, "Purchases.Manage") || Permissions.Has(_user.Role, "Purchases.Return") ? Visibility.Visible : Visibility.Collapsed;
        QuotationsNavButton.Visibility = Permissions.Has(_user.Role, "Sales.Quote") ? Visibility.Visible : Visibility.Collapsed;
        SalesNavButton.Visibility = Permissions.Has(_user.Role, "Sales.View") ? Visibility.Visible : Visibility.Collapsed;
        ShiftNavButton.Visibility = Permissions.Has(_user.Role, "Shift.Manage") ? Visibility.Visible : Visibility.Collapsed;
        ReportsNavButton.Visibility = Permissions.Has(_user.Role, "Reports.View") ? Visibility.Visible : Visibility.Collapsed;
        OperationsNavButton.Visibility = DashboardAccessPolicy.CanOpenOperations(_user.Role) ? Visibility.Visible : Visibility.Collapsed;
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
        ProfitChartCard.Visibility = canViewProfit ? Visibility.Visible : Visibility.Collapsed;
        ApplyResponsiveLayout();
        Loaded += async (_, _) => await _viewModel.RefreshAsync(canViewProfit);
    }

    private void ApplyResponsiveLayout(double availableWidth = 0)
    {
        var width = availableWidth > 0
            ? availableWidth
            : DashboardContent.ActualWidth > 0
                ? DashboardContent.ActualWidth
                : Math.Max(0, Width - 205 - 44 - 32);
        var metricColumns = width < 620 ? 1 : width < 900 ? 2 : width < 1500 ? 3 : 4;
        LayoutCards(MetricsGrid,
            [SalesMetricCard, PurchasesMetricCard, ProfitMetricCard, ExpensesMetricCard,
                ProductsMetricCard, LowStockMetricCard, CustomersMetricCard, SuppliersMetricCard], metricColumns);

        var chartColumns = width < 1020 ? 1 : 3;
        LayoutCards(ChartsGrid, [SalesChartCard, PurchasesChartCard, ProfitChartCard], chartColumns);
        LayoutCards(RecentActivityGrid, [TopProductsCard, RecentSalesCard, RecentPurchasesCard], chartColumns);
    }

    private static void LayoutCards(Grid grid, IReadOnlyList<FrameworkElement> cards, int preferredColumns)
    {
        var visibleCards = cards.Where(card => card.Visibility != Visibility.Collapsed).ToArray();
        if (visibleCards.Length == 0) return;
        var columns = Math.Clamp(preferredColumns, 1, visibleCards.Length);
        var rows = (visibleCards.Length + columns - 1) / columns;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < visibleCards.Length; index++)
        {
            Grid.SetColumn(visibleCards[index], index % columns);
            Grid.SetRow(visibleCards[index], index / columns);
        }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    private async void Operations_Click(object sender, RoutedEventArgs e)
    {
        if (!DashboardAccessPolicy.CanOpenOperations(_user.Role))
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
    private async void Quotations_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Sales.Quote")) { MessageBox.Show("Your role cannot manage quotations.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
        {
            var services = scope.ServiceProvider;
            new QuotationsWindow(services.GetRequiredService<QuotationService>(), services.GetRequiredService<ProductService>(),
                services.GetRequiredService<CustomerService>(), services.GetRequiredService<SaleService>(), _user) { Owner = this }.ShowDialog();
        }
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Shift_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Shift.Manage")) { MessageBox.Show("Your role cannot manage shifts.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        using (var scope = _scopeFactory.CreateScope())
            new ShiftWindow(scope.ServiceProvider.GetRequiredService<ShiftService>(), _user) { Owner = this }.ShowDialog();
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
