using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class PurchaseWindow : Window
{
    public PurchaseWindow(ProductService products, SupplierService suppliers, PurchaseService purchases, PurchaseReturnsService purchaseReturns, AppUser user)
    {
        InitializeComponent(); Title = $"{Convert.ToString(TryFindResource("AppName")) ?? "SmartPOS"} | Receive purchase";
        DataContext = new PurchaseViewModel(products, suppliers, purchases, purchaseReturns, user);
        ReceiveTab.Visibility = SmartPOS.Core.Services.Permissions.Has(user.Role, "Purchases.Manage") ? Visibility.Visible : Visibility.Collapsed;
        ReturnTab.Visibility = SmartPOS.Core.Services.Permissions.Has(user.Role, "Purchases.Return") ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await ((PurchaseViewModel)DataContext).InitializeAsync();
    }
    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        var command = ((PurchaseViewModel)DataContext).ReceiveCommand;
        if (!command.CanExecute(null)) return;
        if (MessageBox.Show("Receive this purchase and increase product stock?", "Confirm purchase", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            command.Execute(null);
    }
    private void Return_Click(object sender, RoutedEventArgs e)
    {
        var command = ((PurchaseViewModel)DataContext).ReturnPurchaseCommand;
        if (!command.CanExecute(null)) return;
        if (MessageBox.Show("Process this supplier return and reduce product stock?", "Confirm purchase return", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            command.Execute(null);
    }
}
