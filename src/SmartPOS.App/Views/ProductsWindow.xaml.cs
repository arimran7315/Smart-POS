using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Infrastructure.Services;
using SmartPOS.Core.Entities;
using SmartPOS.App.Services;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.App.Views;

public partial class ProductsWindow : Window
{
    private readonly ProductsViewModel _viewModel;
    private readonly BarcodeLabelPrinter _barcodePrinter = new();
    private readonly PosDbContext _db;
    private readonly AppUser _user;
    public ProductsWindow(ProductService products, InventoryService inventory, CatalogTaxonomyService taxonomy, PosDbContext db, AppUser user)
    {
        InitializeComponent(); _db = db; _user = user; _viewModel = new ProductsViewModel(products, inventory, taxonomy, user.Id, user.Role); DataContext = _viewModel;
        DeactivateButton.IsEnabled = SmartPOS.Core.Services.Permissions.Has(user.Role, "Products.Manage");
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
    private async void PrintBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProduct is not { Id: > 0 } product) { MessageBox.Show("Save or select a product first."); return; }
        if (!int.TryParse(LabelCopiesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies)) { MessageBox.Show("Enter a valid label count."); return; }
        try
        {
            if (_barcodePrinter.Print(this, product, copies))
            {
                _db.AuditEntries.Add(new SmartPOS.Core.Entities.AuditEntry { UserId = _user.Id, Action = "Print", EntityName = "Product", EntityId = product.Id.ToString(), Description = $"Printed {copies} barcode label(s) for {product.Sku}." });
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Barcode label printing failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.DeactivateCommand.CanExecute(null)) return;
        if (MessageBox.Show("Deactivate this product? Historical sales will remain unchanged.", "Confirm deactivation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _viewModel.DeactivateCommand.Execute(null);
    }
}
