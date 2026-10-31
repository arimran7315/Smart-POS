using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.App.Views;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App;

public partial class PosWindow : Window
{
    private readonly SaleService _sales;
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly CatalogTaxonomyService _taxonomy;
    private readonly AppUser _user;
    private readonly ReceiptPrinter _receiptPrinter;
    private readonly PosViewModel _viewModel;

    public PosWindow(SaleService sales, HeldSaleService heldSales, ProductService products, InventoryService inventory,
        CatalogTaxonomyService taxonomy, PosScreenService screen, AppUser user, ReceiptPrinter receiptPrinter)
    {
        InitializeComponent();
        WindowLayout.FitToWorkArea(this);
        Title = $"{SmartPosIdentity.Name} | Point of Sale";
        _sales = sales; _products = products; _inventory = inventory; _taxonomy = taxonomy; _user = user;
        _receiptPrinter = receiptPrinter;
        _viewModel = new PosViewModel(products, heldSales, sales, screen, user, new WpfUserConfirmationService(this));
        _viewModel.SaleCompleted += message => ToastNotifications.Show(this, message);
        DataContext = _viewModel;
        var canApplyDiscount = Permissions.Has(user.Role, "Sales.Discount");
        DiscountColumn.IsReadOnly = !canApplyDiscount; BillDiscountBox.IsReadOnly = !canApplyDiscount;
        Loaded += async (_, _) => await _viewModel.InitializeAsync(SmartPosIdentity.Name);
    }

    private void Products_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Products.Manage"))
        {
            ThemedMessageBox.Show("Your role cannot manage products.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        new ProductsWindow(_products, _inventory, _taxonomy, _user) { Owner = this }.ShowDialog();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await _viewModel.SearchProductsCommand.ExecuteAsync(); }
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastSale is null) return;
        try
        {
            var receipt = await _sales.GetReceiptForReprintAsync(_viewModel.LastSale.Id, _user.Id);
            var settings = receipt.Settings;
            var paperWidth = double.TryParse(settings.GetValueOrDefault("ReceiptWidthMm"), NumberStyles.Number, CultureInfo.InvariantCulture, out var mm) ? mm : 80;
            var storeName = settings.GetValueOrDefault("StoreName") ?? SmartPosIdentity.Name;
            if (_receiptPrinter.Print(this, receipt.Sale, storeName, settings.GetValueOrDefault("StoreAddress"), settings.GetValueOrDefault("StorePhone"), receipt.CashierName,
                settings.GetValueOrDefault("Currency") ?? "", paperWidth, receipt.CustomerName, settings.GetValueOrDefault("TaxNumber"), settings.GetValueOrDefault("DefaultPrinter"), settings.GetValueOrDefault("ReceiptPrintMode"), settings.GetValueOrDefault("StoreEmail")))
            {
                try { await _sales.RecordReceiptPrintedAsync(_viewModel.LastSale.Id, _user.Id); }
                catch (Exception ex)
                {
                    var detail = UserErrorHandler.GetMessage(ex, "Recording the receipt print");
                    ThemedMessageBox.Show($"The receipt was sent to the printer, but its activity entry could not be saved. Do not print again just because of this message. {detail}",
                        "Receipt sent; activity log failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex) { ThemedMessageBox.Show(UserErrorHandler.GetMessage(ex, "Printing the receipt"), "Receipt printing failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1: SearchBox.Focus(); e.Handled = true; break;
            case Key.F2: SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; break;
            case Key.F3: await _viewModel.HoldSaleCommand.ExecuteAsync(); e.Handled = true; break;
            case Key.F4: PaymentBox.Focus(); PaymentBox.IsDropDownOpen = true; e.Handled = true; break;
            case Key.F5: await _viewModel.NewSaleCommand.ExecuteAsync(); e.Handled = true; break;
            case Key.F8 when _viewModel.LastSale is not null: Print_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Delete when _viewModel.SelectedCartLine is not null && !IsTextEntryFocused():
                await _viewModel.RemoveProductCommand.ExecuteAsync(); e.Handled = true; break;
        }
    }

    private static bool IsTextEntryFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (focused is TextBoxBase or PasswordBox or ComboBox) return true;
            focused = focused is Visual or Visual3D
                ? VisualTreeHelper.GetParent(focused) ?? LogicalTreeHelper.GetParent(focused)
                : LogicalTreeHelper.GetParent(focused);
        }
        return false;
    }
}
