using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class PurchaseLineRow(int productId, string name, decimal quantity, decimal cost, decimal discountRate, decimal taxRate)
{
    public int ProductId { get; } = productId;
    public string ProductName { get; } = name;
    public decimal Quantity { get; } = quantity;
    public decimal UnitCost { get; } = cost;
    public decimal DiscountRate { get; } = discountRate;
    public decimal TaxRate { get; } = taxRate;
    public decimal Discount => decimal.Round(Quantity * UnitCost * DiscountRate / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal Tax => decimal.Round((Quantity * UnitCost - Discount) * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal Total => decimal.Round(Quantity * UnitCost - Discount + Tax, 2, MidpointRounding.AwayFromZero);
}

public sealed class PurchaseReturnLineRow(int purchaseItemId, string name, decimal quantity, decimal total)
{
    public int PurchaseItemId { get; } = purchaseItemId;
    public string ProductName { get; } = name;
    public decimal Quantity { get; } = quantity;
    public decimal UnitReturnValue => Quantity == 0 ? 0 : Total / Quantity;
    public decimal Total { get; } = total;
}

public sealed class PurchaseViewModel : INotifyPropertyChanged
{
    private readonly ProductService _products; private readonly SupplierService _suppliers; private readonly PurchaseService _purchases; private readonly PurchaseReturnsService _purchaseReturns; private readonly SupplierPaymentService _supplierPayments; private readonly int _userId; private readonly string _role;
    private readonly IUserConfirmationService _confirmation;
    private Product? _selectedProduct; private Supplier? _selectedSupplier; private PurchaseLineRow? _selectedLine; private Purchase? _selectedPurchase; private ReturnablePurchaseItem? _selectedReturnItem; private PurchaseReturnLineRow? _selectedReturnLine;
    private string _quantity = "1", _unitCost = "0", _discountRate = "0", _taxRate = "0", _returnQuantity = "1", _returnReason = "", _status = "";
    private OutstandingPurchasePayment? _selectedOpenPurchase;
    private string _supplierPaymentAmount = "", _supplierPaymentMethod = "Cash", _supplierPaymentReference = "";
    private string _initialPaymentAmount = "0", _initialPaymentMethod = "Cash", _initialPaymentReference = "";
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "Card", "Bank transfer"];
    public bool CanMakePayments => Permissions.Has(_role, "Payments.Make");
    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<PurchaseLineRow> Lines { get; } = [];
    public ObservableCollection<Purchase> Purchases { get; } = [];
    public ObservableCollection<OutstandingPurchasePayment> OutstandingPurchases { get; } = [];
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
    public string DiscountRate { get => _discountRate; set { _discountRate = value; Changed(); } }
    public string TaxRate { get => _taxRate; set { _taxRate = value; Changed(); } }
    public string ReturnQuantity { get => _returnQuantity; set { _returnQuantity = value; Changed(); } }
    public string ReturnReason { get => _returnReason; set { _returnReason = value; Changed(); ReturnPurchaseCommand?.Refresh(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public OutstandingPurchasePayment? SelectedOpenPurchase { get => _selectedOpenPurchase; set { _selectedOpenPurchase = value; Changed(); RecordPurchasePaymentCommand?.Refresh(); } }
    public string SupplierPaymentAmount { get => _supplierPaymentAmount; set { _supplierPaymentAmount = value; Changed(); RecordPurchasePaymentCommand?.Refresh(); } }
    public string SupplierPaymentMethod { get => _supplierPaymentMethod; set { _supplierPaymentMethod = value; Changed(); } }
    public string SupplierPaymentReference { get => _supplierPaymentReference; set { _supplierPaymentReference = value; Changed(); } }
    public string InitialPaymentAmount { get => _initialPaymentAmount; set { _initialPaymentAmount = value; Changed(); ReceiveCommand?.Refresh(); } }
    public string InitialPaymentMethod { get => _initialPaymentMethod; set { _initialPaymentMethod = value; Changed(); } }
    public string InitialPaymentReference { get => _initialPaymentReference; set { _initialPaymentReference = value; Changed(); } }
    public decimal Total => Lines.Sum(x => x.Total);
    public AsyncCommand AddLineCommand { get; }
    public AsyncCommand RemoveLineCommand { get; }
    public AsyncCommand ReceiveCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadReturnItemsCommand { get; }
    public AsyncCommand AddReturnLineCommand { get; }
    public AsyncCommand RemoveReturnLineCommand { get; }
    public AsyncCommand ReturnPurchaseCommand { get; }
    public AsyncCommand RefreshOpenPurchasesCommand { get; }
    public AsyncCommand RecordPurchasePaymentCommand { get; }
    public decimal ReturnTotal => ReturnLines.Sum(x => x.Total);
    public PurchaseViewModel(ProductService products, SupplierService suppliers, PurchaseService purchases, PurchaseReturnsService purchaseReturns, SupplierPaymentService supplierPayments, AppUser user, IUserConfirmationService confirmation)
    {
        _products = products; _suppliers = suppliers; _purchases = purchases; _purchaseReturns = purchaseReturns; _supplierPayments = supplierPayments; _userId = user.Id; _role = user.Role; _confirmation = confirmation;
        AddLineCommand = new(AddLine, () => SelectedProduct is not null && Permissions.Has(_role, "Purchases.Manage"));
        RemoveLineCommand = new(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); SelectedLine = null; Changed(nameof(Total)); ReceiveCommand?.Refresh(); return Task.CompletedTask; });
        ReceiveCommand = new(ReceiveAsync, () => SelectedSupplier is not null && Lines.Count > 0 && Permissions.Has(_role, "Purchases.Manage")
            && decimal.TryParse(InitialPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var paid)
            && paid >= 0 && paid <= Total && (paid == 0 || CanMakePayments));
        RefreshCommand = new(LoadAsync);
        LoadReturnItemsCommand = new(LoadReturnItemsAsync, () => SelectedPurchase is not null && Permissions.Has(_role, "Purchases.Return"));
        AddReturnLineCommand = new(AddReturnLine, () => SelectedReturnItem is not null && Permissions.Has(_role, "Purchases.Return"));
        RemoveReturnLineCommand = new(() => { if (SelectedReturnLine is not null) ReturnLines.Remove(SelectedReturnLine); SelectedReturnLine = null; Changed(nameof(ReturnTotal)); ReturnPurchaseCommand?.Refresh(); return Task.CompletedTask; });
        ReturnPurchaseCommand = new(ReturnAsync, () => SelectedPurchase is not null && ReturnLines.Count > 0 && !string.IsNullOrWhiteSpace(ReturnReason) && Permissions.Has(_role, "Purchases.Return"));
        RefreshOpenPurchasesCommand = new(LoadOpenPurchasesAsync, () => Permissions.Has(_role, "Payments.Make"));
        RecordPurchasePaymentCommand = new(RecordPurchasePaymentAsync, () => SelectedOpenPurchase is not null
            && decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            && amount > 0 && amount <= SelectedOpenPurchase.RemainingAmount && Permissions.Has(_role, "Payments.Make"));
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
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading purchase data"); }
        if (Permissions.Has(_role, "Payments.Make")) await LoadOpenPurchasesAsync();
    }
    private async Task LoadOpenPurchasesAsync()
    {
        try
        {
            var selectedId = SelectedOpenPurchase?.PurchaseId;
            OutstandingPurchases.Clear();
            foreach (var purchase in await _supplierPayments.SearchOpenPurchasesAsync(_userId)) OutstandingPurchases.Add(purchase);
            SelectedOpenPurchase = OutstandingPurchases.FirstOrDefault(x => x.PurchaseId == selectedId);
            Status = OutstandingPurchases.Count == 0 ? "There are no outstanding purchase invoices." : $"Loaded {OutstandingPurchases.Count} outstanding purchase invoice(s).";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading outstanding supplier invoices"); }
    }
    private async Task RecordPurchasePaymentAsync()
    {
        if (SelectedOpenPurchase is null || !decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) return;
        try
        {
            await _supplierPayments.RecordForPurchaseAsync(SelectedOpenPurchase.PurchaseId, amount, _userId, SupplierPaymentMethod, SupplierPaymentReference);
            SupplierPaymentAmount = ""; SupplierPaymentReference = "";
            Status = "Supplier invoice payment recorded.";
            await LoadOpenPurchasesAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording the invoice payment"); }
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
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading returnable items"); }
    }
    private Task AddReturnLine()
    {
        if (SelectedReturnItem is null || !decimal.TryParse(ReturnQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0)
        { Status = "Choose an item and enter a positive return quantity."; return Task.CompletedTask; }
        var existingLines = ReturnLines.Where(x => x.PurchaseItemId == SelectedReturnItem.PurchaseItemId).ToArray();
        var remaining = SelectedReturnItem.QuantityRemaining - existingLines.Sum(x => x.Quantity);
        if (quantity > remaining) { Status = $"Only {remaining:N3} unit(s) remain available to return."; return Task.CompletedTask; }
        var remainingValue = SelectedReturnItem.ValueRemaining - existingLines.Sum(x => x.Total);
        var value = quantity >= remaining ? remainingValue
            : decimal.Round(remainingValue * quantity / remaining, 2, MidpointRounding.AwayFromZero);
        ReturnLines.Add(new(SelectedReturnItem.PurchaseItemId, SelectedReturnItem.ProductName, quantity, value));
        ReturnQuantity = "1"; Changed(nameof(ReturnTotal)); ReturnPurchaseCommand.Refresh(); Status = "Return item added.";
        return Task.CompletedTask;
    }
    private async Task ReturnAsync()
    {
        if (SelectedPurchase is null) return;
        var purchase = SelectedPurchase;
        if (!await _confirmation.ConfirmAsync("Confirm purchase return",
                $"Process a supplier return for invoice {purchase.InvoiceNumber}, total {ReturnTotal:N2}? Product stock will be reduced.",
                ConfirmationKind.Warning)) return;
        try
        {
            var result = await _purchaseReturns.ReturnAsync(purchase.Id, _userId,
                ReturnLines.Select(x => (x.PurchaseItemId, x.Quantity)).ToArray(), ReturnReason);
            Status = $"Processed {result.ReturnNumber} for {result.Total:N2}; stock was reduced.";
            ReturnLines.Clear(); ReturnReason = ""; Changed(nameof(ReturnTotal));
            await LoadAsync();
            if (SelectedPurchase is not null) await LoadReturnItemsAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Processing the purchase return"); }
    }
    private Task AddLine()
    {
        if (!decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0) { Status = "Enter a positive quantity."; return Task.CompletedTask; }
        if (!decimal.TryParse(UnitCost, NumberStyles.Number, CultureInfo.CurrentCulture, out var cost) || cost < 0) { Status = "Enter a valid non-negative unit cost."; return Task.CompletedTask; }
        if (!decimal.TryParse(DiscountRate, NumberStyles.Number, CultureInfo.CurrentCulture, out var discount) || discount is < 0 or > 100)
        { Status = "Discount rate must be between 0 and 100 percent."; return Task.CompletedTask; }
        if (!decimal.TryParse(TaxRate, NumberStyles.Number, CultureInfo.CurrentCulture, out var tax) || tax is < 0 or > 100)
        { Status = "Tax rate must be between 0 and 100 percent."; return Task.CompletedTask; }
        var product = SelectedProduct!; var line = Lines.FirstOrDefault(x => x.ProductId == product.Id && x.UnitCost == cost && x.DiscountRate == discount && x.TaxRate == tax);
        if (line is not null) { Lines.Remove(line); Lines.Add(new PurchaseLineRow(product.Id, product.Name, line.Quantity + quantity, cost, discount, tax)); }
        else Lines.Add(new PurchaseLineRow(product.Id, product.Name, quantity, cost, discount, tax));
        Quantity = "1"; DiscountRate = "0"; TaxRate = "0"; Changed(nameof(Total)); ReceiveCommand.Refresh(); Status = "Item added to purchase."; return Task.CompletedTask;
    }
    private async Task ReceiveAsync()
    {
        try
        {
            if (!decimal.TryParse(InitialPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var paid) || paid < 0 || paid > Total)
            { Status = "Enter an initial payment from zero through the invoice total."; return; }
            var supplier = SelectedSupplier;
            if (supplier is null) return;
            if (!await _confirmation.ConfirmAsync("Confirm purchase",
                    $"Receive this purchase from {supplier.Name} for {Total:N2} and increase product stock?",
                    ConfirmationKind.Question)) return;
            var purchase = await _purchases.ReceiveWithPaymentAsync(supplier.Id, _userId,
                Lines.Select(x => new PurchaseLineInput(x.ProductId, x.Quantity, x.UnitCost, x.DiscountRate, x.TaxRate)).ToArray(),
                paid, InitialPaymentMethod, InitialPaymentReference);
            Status = $"Received {purchase.InvoiceNumber} for {purchase.Total:N2}; paid {purchase.PaidAmount:N2}, remaining {purchase.RemainingAmount:N2}. Stock has been updated.";
            Lines.Clear(); InitialPaymentAmount = "0"; InitialPaymentReference = ""; Changed(nameof(Total)); ReceiveCommand.Refresh();
            await LoadAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Receiving the purchase"); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
