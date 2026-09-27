using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.App.Views;
using SmartPOS.App.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App;

public partial class PosWindow : Window
{
    private readonly SaleService _sales; private readonly ProductService _products; private readonly PosDbContext _db; private readonly AppUser _user;
    private readonly ReceiptPrinter _receiptPrinter = new();
    private Sale? _lastSale;
    private bool _ready;
    private readonly ObservableCollection<CartLine> _cart = [];
    public PosWindow(SaleService sales, ProductService products, PosDbContext db, AppUser user)
    {
        InitializeComponent(); _sales = sales; _products = products; _db = db; _user = user; CartGrid.ItemsSource = _cart;
        _ready = true;
        CashierInfo.Text = $"{user.DisplayName} · {user.Role}"; StoreInfo.Text = Convert.ToString(TryFindResource("AppName")) ?? "SmartPOS"; UpdateTotals();
        Loaded += async (_, _) =>
        {
            var settings = await _db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
            if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("StoreName"))) StoreInfo.Text = settings["StoreName"];
            CustomerBox.Items.Clear(); CustomerBox.Items.Add(new ComboBoxItem { Content = "Walk-in", Tag = "" });
            foreach (var customer in await _db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync())
                CustomerBox.Items.Add(new ComboBoxItem { Content = customer.Name, Tag = customer.Id.ToString(CultureInfo.InvariantCulture) });
            CustomerBox.SelectedIndex = 0;
        };
    }
    private void Products_Click(object sender, RoutedEventArgs e)
    {
        if (!Permissions.Has(_user.Role, "Products.Manage")) { MessageBox.Show("Your role cannot manage products.", "Permission denied", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        new ProductsWindow(_products, new InventoryService(_db), new CatalogTaxonomyService(_db), _db, _user) { Owner = this }.ShowDialog();
    }
    private async void Add_Click(object sender, RoutedEventArgs e) => await AddProductAsync();
    private async void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await AddProductAsync(); } }
    private async Task AddProductAsync()
    {
        var key = SearchBox.Text.Trim(); if (key.Length == 0) return;
        var matches = await _db.Products.Where(p => p.IsActive && (p.Barcode == key || p.Sku == key || p.Name.Contains(key))).OrderBy(p => p.Name).Take(2).ToListAsync();
        if (matches.Count == 0) { SearchMessage.Text = "No active product matches that barcode, SKU, or name."; return; }
        Product? product = matches.Count == 1 ? matches[0] : await ChooseProductAsync(matches);
        if (product is null) return;
        if (product.CurrentStock <= 0) { SearchMessage.Text = $"{product.Name} is out of stock."; return; }
        var existing = _cart.FirstOrDefault(x => x.ProductId == product.Id);
        if (existing is not null) { if (existing.Quantity + 1 > product.CurrentStock) { SearchMessage.Text = $"Only {product.CurrentStock} in stock."; return; } existing.Quantity++; }
        else _cart.Add(new CartLine(product.Id, product.Name, 1, product.SalePrice, product.PurchasePrice, product.TaxRate, product.DiscountRate, product.CurrentStock));
        SearchMessage.Text = ""; SearchBox.Clear(); UpdateTotals(); SearchBox.Focus();
    }
    private async Task<Product?> ChooseProductAsync(List<Product> products)
    {
        var picker = new Window { Title = "Choose product", Width = 520, Height = 340, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { ItemsSource = products, DisplayMemberPath = "Name", Margin = new Thickness(10) }; list.MouseDoubleClick += (_, _) => picker.DialogResult = true; picker.Content = list;
        return picker.ShowDialog() == true ? list.SelectedItem as Product : null;
    }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (CartGrid.SelectedItem is CartLine line) _cart.Remove(line); UpdateTotals(); }
    private void PaidBox_TextChanged(object sender, TextChangedEventArgs e) { if (_ready) UpdateTotals(); }
    private void PaymentBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateTotals(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { _cart.Clear(); UpdateTotals(); SearchMessage.Text = "Cart cleared."; }
    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0) { SearchMessage.Text = "Add a product before completing the sale."; return; }
        if (!decimal.TryParse(PaidBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var paid) || paid < 0) { MessageBox.Show("Enter a valid non-negative amount received."); return; }
        var method = ((ComboBoxItem)PaymentBox.SelectedItem).Content.ToString()!;
        var customerTag = (CustomerBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        int? customerId = int.TryParse(customerTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCustomerId) ? parsedCustomerId : null;
        if (method == "Credit" && customerId is null) { MessageBox.Show("Select a customer to make a credit sale."); return; }
        var totals = SaleCalculator.Calculate(_cart.Select(x => new SaleLine(x.Quantity, x.UnitPrice, x.UnitCost, x.DiscountRate, x.TaxRate)));
        if (method != "Credit" && paid < totals.GrandTotal) { MessageBox.Show("Amount received is less than the sale total."); return; }
        if (method == "Credit" && !Permissions.Has(_user.Role, "Sales.Credit")) { MessageBox.Show("Your role cannot create credit sales."); return; }
        if (MessageBox.Show($"Complete sale for {totals.GrandTotal:N2}?", "Confirm sale", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var sale = await _sales.CompleteAsync(_cart.Select(x => (x.ProductId, x.Quantity)).ToArray(), _user.Id, customerId, 0, paid, method, null);
            _lastSale = sale; PrintButton.IsEnabled = true;
            MessageBox.Show($"Sale {sale.InvoiceNumber} saved. Change: {SaleCalculator.Change(paid, sale.GrandTotal):N2}", "Sale complete", MessageBoxButton.OK, MessageBoxImage.Information);
            _cart.Clear(); PaidBox.Text = "0"; UpdateTotals(); SearchBox.Focus();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Sale failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSale is null) return;
        var settings = await _db.Settings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
        var paperWidth = double.TryParse(settings.GetValueOrDefault("ReceiptWidthMm"), out var mm) ? mm : 80;
        try
        {
            if (_receiptPrinter.Print(this, _lastSale, StoreInfo.Text, settings.GetValueOrDefault("StoreAddress"), settings.GetValueOrDefault("StorePhone"), _user.DisplayName,
                settings.GetValueOrDefault("Currency") ?? "", paperWidth))
            {
                _db.AuditEntries.Add(new AuditEntry { UserId = _user.Id, Action = "Print", EntityName = "Sale", EntityId = _lastSale.InvoiceNumber, Description = $"Printed receipt for {_lastSale.InvoiceNumber}." });
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Receipt printing failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1) { SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.F4) { Complete_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F5) { Clear_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Delete && CartGrid.SelectedItem is not null) { Remove_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }
    private void UpdateTotals()
    {
        var t = SaleCalculator.Calculate(_cart.Select(x => new SaleLine(x.Quantity, x.UnitPrice, x.UnitCost, x.DiscountRate, x.TaxRate)));
        SubtotalText.Text = t.Subtotal.ToString("N2"); TaxText.Text = t.Tax.ToString("N2"); TotalText.Text = t.GrandTotal.ToString("N2");
        var paid = decimal.TryParse(PaidBox?.Text, out var p) ? p : 0;
        var method = (PaymentBox?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        ChangeText.Text = $"Change: {(method == "Cash" ? SaleCalculator.Change(paid, t.GrandTotal) : 0):N2}";
    }
    private sealed class CartLine(int productId, string productName, decimal quantity, decimal unitPrice, decimal unitCost, decimal taxRate, decimal discountRate, decimal available) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int ProductId { get; } = productId; public string ProductName { get; } = productName;
        private decimal _quantity = quantity;
        public decimal Quantity { get => _quantity; set { if (_quantity == value) return; _quantity = value; PropertyChanged?.Invoke(this, new(nameof(Quantity))); PropertyChanged?.Invoke(this, new(nameof(LineTotal))); } }
        public decimal UnitPrice { get; } = unitPrice; public decimal UnitCost { get; } = unitCost; public decimal TaxRate { get; } = taxRate;
        public decimal DiscountRate { get; } = discountRate; public decimal Available { get; } = available; public decimal LineTotal => Quantity * UnitPrice * (1 - DiscountRate / 100m) * (1 + TaxRate / 100m);
    }
}
