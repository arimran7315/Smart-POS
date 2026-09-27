using System.Windows;
using System.Windows.Input;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class DashboardWindow : Window
{
    private readonly DashboardService _dashboard;
    private readonly SaleService _sales;
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly PosDbContext _db;
    private readonly AppUser _user;
    private readonly DashboardViewModel _viewModel;
    public DashboardWindow(DashboardService dashboard, SaleService sales, ProductService products, InventoryService inventory, PosDbContext db, AppUser user)
    {
        InitializeComponent(); _dashboard = dashboard; _sales = sales; _products = products; _inventory = inventory; _db = db; _user = user;
        UserInfo.Text = $"{user.DisplayName} · {user.Role}"; _viewModel = new DashboardViewModel(dashboard, user.Id); DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    private async void Operations_Click(object sender, RoutedEventArgs e)
    {
        if (!new[] { "Customers.Manage", "Suppliers.Manage", "Expenses.Manage", "Payments.Receive" }.Any(p => Permissions.Has(_user.Role, p)))
        { MessageBox.Show("Your role cannot access business operations.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new BackOfficeWindow(_db, _user) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Sales_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Sales.View")) { MessageBox.Show("Your role cannot view sales.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new SalesHistoryWindow(_sales, _db, _user) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Purchase_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Purchases.Manage") && !Permissions.Has(_user.Role, "Purchases.Return")) { MessageBox.Show("Your role cannot access purchases.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new PurchaseWindow(_products, new SupplierService(_db), new PurchaseService(_db), new PurchaseReturnsService(_db), _user) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private async void Reports_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Reports.View")) { MessageBox.Show("Your role cannot view reports.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new ReportsWindow(new ReportsService(_db), _user) { Owner = this }.ShowDialog();
        await _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    private void Products_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Products.Manage")) { MessageBox.Show("Your role cannot manage products.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new ProductsWindow(_products, _inventory, new CatalogTaxonomyService(_db), _db, _user) { Owner = this }.ShowDialog();
        Refresh_Click(sender, e);
    }
    private void Pos_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Sales.Create")) { MessageBox.Show("Your role cannot create sales.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new PosWindow(_sales, _products, _db, _user) { Owner = this }.ShowDialog();
        _ = _viewModel.RefreshAsync(Permissions.Has(_user.Role, "Reports.Profit"));
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (e.Key == Key.F1) { Pos_Click(this, new RoutedEventArgs()); e.Handled = true; } base.OnPreviewKeyDown(e); }
}
