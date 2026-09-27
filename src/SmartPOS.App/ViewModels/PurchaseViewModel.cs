using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class PurchaseLineRow(int productId, string name, decimal quantity, decimal cost)
{
    public int ProductId { get; } = productId;
    public string ProductName { get; } = name;
    public decimal Quantity { get; } = quantity;
    public decimal UnitCost { get; } = cost;
    public decimal Total => Quantity * UnitCost;
}

public sealed class PurchaseReturnLineRow(int purchaseItemId, string name, decimal quantity, decimal unitCost)
{
    public int PurchaseItemId { get; } = purchaseItemId;
    public string ProductName { get; } = name;
    public decimal Quantity { get; } = quantity;
    public decimal UnitCost { get; } = unitCost;
    public decimal Total => Quantity * UnitCost;
}

public sealed class PurchaseViewModel : INotifyPropertyChanged
{
    private readonly ProductService _products; private readonly SupplierService _suppliers; private readonly PurchaseService _purchases; private readonly PurchaseReturnsService _purchaseReturns; private readonly int _userId; private readonly string _role;
    private Product? _selectedProduct; private Supplier? _selectedSupplier; private PurchaseLineRow? _selectedLine; private Purchase? _selectedPurchase; private ReturnablePurchaseItem? _selectedReturnItem; private PurchaseReturnLineRow? _selectedReturnLine;
    private string _quantity = "1", _unitCost = "0", _returnQuantity = "1", _returnReason = "", _status = "";
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<PurchaseLineRow> Lines { get; } = [];
    public ObservableCollection<Purchase> Purchases { get; } = [];
    public ObservableCollection<ReturnablePurchaseItem> ReturnableItems { get; } = [];
    public ObservableCollection<PurchaseReturnLineRow> ReturnLines { get; } = [];
    public Product? SelectedProduct { get => _selectedProduct; set { _selectedProduct = value; if (value is not null) UnitCost = value.PurchasePrice.ToString(CultureInfo.CurrentCulture); Changed(); AddLineCommand?.Refresh(); } }
    public Supplier? SelectedSupplier { get => _selectedSupplier; set { _selectedSupplier = value; Changed(); ReceiveCommand?.Refresh(); } }
    public PurchaseLineRow? SelectedLine { get => _selectedLine; set { _selectedLine = value; Changed(); RemoveLineCommand?.Refresh(); } }
    public Purchase? SelectedPurchase { get => _selectedPurchase; set { _selectedPurchase = value; Changed(); LoadReturnItemsCommand?.Refresh(); ReturnPurchaseCommand?.Refresh(); } }
    public ReturnablePurchaseItem? SelectedReturnItem { get => _selectedReturnItem; set { _selectedReturnItem = value; Changed(); AddReturnLineCommand?.Refresh(); } }
    public PurchaseReturnLineRow? SelectedReturnLine { get => _selectedReturnLine; set { _selectedReturnLine = value; Changed(); RemoveReturnLineCommand?.Refresh(); } }
    public string Quantity { get => _quantity; set { _quantity = value; Changed(); } }
    public string UnitCost { get => _unitCost; set { _unitCost = value; Changed(); } }
    public string ReturnQuantity { get => _returnQuantity; set { _returnQuantity = value; Changed(); } }
    public string ReturnReason { get => _returnReason; set { _returnReason = value; Changed(); ReturnPurchaseCommand?.Refresh(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public decimal Total => Lines.Sum(x => x.Total);
    public AsyncCommand AddLineCommand { get; }
    public AsyncCommand RemoveLineCommand { get; }
    public AsyncCommand ReceiveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadReturnItemsCommand { get; }
    public AsyncCommand AddReturnLineCommand { get; }
    public AsyncCommand RemoveReturnLineCommand { get; }
    public AsyncCommand ReturnPurchaseCommand { get; }
    public decimal ReturnTotal => ReturnLines.Sum(x => x.Total);
    public PurchaseViewModel(ProductService products, SupplierService suppliers, PurchaseService purchases, PurchaseReturnsService purchaseReturns, AppUser user)
    {
        _products = products; _suppliers = suppliers; _purchases = purchases; _purchaseReturns = purchaseReturns; _userId = user.Id; _role = user.Role;
        AddLineCommand = new(AddLine, () => SelectedProduct is not null && Permissions.Has(_role, "Purchases.Manage"));
        RemoveLineCommand = new(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); SelectedLine = null; Changed(nameof(Total)); ReceiveCommand?.Refresh(); return Task.CompletedTask; });
        ReceiveCommand = new(ReceiveAsync, () => SelectedSupplier is not null && Lines.Count > 0 && Permissions.Has(_role, "Purchases.Manage"));
        RefreshCommand = new(LoadAsync);
        LoadReturnItemsCommand = new(LoadReturnItemsAsync, () => SelectedPurchase is not null && Permissions.Has(_role, "Purchases.Return"));
        AddReturnLineCommand = new(AddReturnLine, () => SelectedReturnItem is not null && Permissions.Has(_role, "Purchases.Return"));
        RemoveReturnLineCommand = new(() => { if (SelectedReturnLine is not null) ReturnLines.Remove(SelectedReturnLine); SelectedReturnLine = null; Changed(nameof(ReturnTotal)); ReturnPurchaseCommand?.Refresh(); return Task.CompletedTask; });
        ReturnPurchaseCommand = new(ReturnAsync, () => SelectedPurchase is not null && ReturnLines.Count > 0 && !string.IsNullOrWhiteSpace(ReturnReason) && Permissions.Has(_role, "Purchases.Return"));
    }
    public async Task InitializeAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            Products.Clear(); foreach (var product in await _products.SearchAsync("", _userId, take: 200)) Products.Add(product);
            Suppliers.Clear(); foreach (var supplier in await _suppliers.SearchAsync("", _userId)) Suppliers.Add(supplier);
            var selectedId = SelectedPurchase?.Id;
            Purchases.Clear();
            if (Permissions.Has(_role, "Purchases.Return")) foreach (var purchase in await _purchaseReturns.SearchPurchasesAsync(_userId)) Purchases.Add(purchase);
            SelectedPurchase = Purchases.FirstOrDefault(x => x.Id == selectedId);
            Status = $"Loaded {Products.Count} product(s), {Suppliers.Count} supplier(s), and {Purchases.Count} returnable purchase(s).";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task LoadReturnItemsAsync()
    {
        if (SelectedPurchase is null) return;
        try
        {
            ReturnableItems.Clear();
            foreach (var item in await _purchaseReturns.GetReturnableItemsAsync(SelectedPurchase.Id, _userId)) ReturnableItems.Add(item);
            ReturnLines.Clear(); Changed(nameof(ReturnTotal)); ReturnPurchaseCommand.Refresh();
            Status = ReturnableItems.Count == 0 ? "No units remain to return for this purchase." : "Choose received items and the quantities being returned.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private Task AddReturnLine()
    {
        if (SelectedReturnItem is null || !decimal.TryParse(ReturnQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0)
        { Status = "Choose an item and enter a positive return quantity."; return Task.CompletedTask; }
        var remaining = SelectedReturnItem.QuantityRemaining - ReturnLines.Where(x => x.PurchaseItemId == SelectedReturnItem.PurchaseItemId).Sum(x => x.Quantity);
        if (quantity > remaining) { Status = $"Only {remaining:N3} unit(s) remain available to return."; return Task.CompletedTask; }
        ReturnLines.Add(new(SelectedReturnItem.PurchaseItemId, SelectedReturnItem.ProductName, quantity, SelectedReturnItem.UnitCost));
        ReturnQuantity = "1"; Changed(nameof(ReturnTotal)); ReturnPurchaseCommand.Refresh(); Status = "Return item added.";
        return Task.CompletedTask;
    }
    private async Task ReturnAsync()
    {
        if (SelectedPurchase is null) return;
        try
        {
            var result = await _purchaseReturns.ReturnAsync(SelectedPurchase.Id, _userId,
                ReturnLines.Select(x => (x.PurchaseItemId, x.Quantity)).ToArray(), ReturnReason);
            Status = $"Processed {result.ReturnNumber} for {result.Total:N2}; stock was reduced.";
            ReturnLines.Clear(); ReturnReason = ""; Changed(nameof(ReturnTotal));
            await LoadAsync();
            if (SelectedPurchase is not null) await LoadReturnItemsAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private Task AddLine()
    {
        if (!decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0) { Status = "Enter a positive quantity."; return Task.CompletedTask; }
        if (!decimal.TryParse(UnitCost, NumberStyles.Number, CultureInfo.CurrentCulture, out var cost) || cost < 0) { Status = "Enter a valid non-negative unit cost."; return Task.CompletedTask; }
        var product = SelectedProduct!; var line = Lines.FirstOrDefault(x => x.ProductId == product.Id && x.UnitCost == cost);
        if (line is not null) { Lines.Remove(line); Lines.Add(new PurchaseLineRow(product.Id, product.Name, line.Quantity + quantity, cost)); }
        else Lines.Add(new PurchaseLineRow(product.Id, product.Name, quantity, cost));
        Quantity = "1"; Changed(nameof(Total)); ReceiveCommand.Refresh(); Status = "Item added to purchase."; return Task.CompletedTask;
    }
    private async Task ReceiveAsync()
    {
        try
        {
            var purchase = await _purchases.ReceiveAsync(SelectedSupplier!.Id, _userId, Lines.Select(x => (x.ProductId, x.Quantity, x.UnitCost)).ToArray());
            Status = $"Received {purchase.InvoiceNumber} for {purchase.Total:N2}. Stock has been updated."; Lines.Clear(); Changed(nameof(Total)); ReceiveCommand.Refresh();
            await LoadAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
