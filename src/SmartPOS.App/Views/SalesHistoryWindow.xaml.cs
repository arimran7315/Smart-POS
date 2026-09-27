using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class SalesHistoryWindow : Window
{
    private readonly SalesHistoryViewModel _viewModel;
    public SalesHistoryWindow(SaleService sales, PosDbContext db, AppUser user)
    {
        InitializeComponent(); Title = $"{Convert.ToString(TryFindResource("AppName")) ?? "SmartPOS"} | Sales and returns";
        ReturnButton.IsEnabled = SmartPOS.Core.Services.Permissions.Has(user.Role, "Sales.Return");
        _viewModel = new SalesHistoryViewModel(sales, new ReturnsService(db), user); DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
    private void Return_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.ReturnCommand.CanExecute(null)) return;
        var sale = _viewModel.SelectedSale!; var item = _viewModel.SelectedItem!;
        if (MessageBox.Show($"Process a return for {item.ProductName} on invoice {sale.InvoiceNumber}? Stock and customer balance/refund will be updated.",
            "Confirm return", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _viewModel.ReturnCommand.Execute(null);
    }
}
