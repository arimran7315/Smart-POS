using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class PosViewModel : INotifyPropertyChanged
{
    private readonly ProductService _products;
    private readonly HeldSaleService _heldSales;
    private readonly SaleService _sales;
    private readonly PosScreenService _screen;
    private readonly AppUser _user;
    private readonly IUserConfirmationService _confirmation;
    private string _storeName = "SmartPOS";
    private int? _selectedCustomerId;
    private string _billDiscountText = "0", _paidText = "0", _mixedCashText = "0", _mixedCardText = "0", _saleNotes = "", _paymentMethod = "Cash";
    private SaleTotals _totals = SaleCalculator.Calculate([]);
    private string _discountError = "", _changeSummary = "Change: 0.00";
    private bool _isDiscountValid = true;
    private HeldSaleSummary? _selectedHeldSale;
    private PosCartLine? _selectedCartLine;
    private Sale? _lastSale;
    private string _statusMessage = "";
    private string _productSearchText = "";
    private SaleProductLookup? _selectedProductMatch;

    public ObservableCollection<PosCustomerOption> Customers { get; } = [];
    public ObservableCollection<HeldSaleSummary> HeldSales { get; } = [];
    public ObservableCollection<SaleProductLookup> ProductMatches { get; } = [];
    public string StoreName { get => _storeName; private set { _storeName = value; Changed(); } }
    public string CashierDisplay => $"{_user.DisplayName} · {_user.Role}";
    public int? SelectedCustomerId { get => _selectedCustomerId; set { _selectedCustomerId = value; Changed(); } }
    public HeldSaleSummary? SelectedHeldSale { get => _selectedHeldSale; set { _selectedHeldSale = value; Changed(); ResumeHeldSaleCommand?.Refresh(); } }
    public PosCartLine? SelectedCartLine { get => _selectedCartLine; set { _selectedCartLine = value; Changed(); RemoveProductCommand?.Refresh(); } }
    public Sale? LastSale { get => _lastSale; private set { _lastSale = value; Changed(); Changed(nameof(CanPrintLastReceipt)); } }
    public bool CanPrintLastReceipt => LastSale is not null;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; Changed(); } }
    public string ProductSearchText
    {
        get => _productSearchText;
        set
        {
            if (_productSearchText == value) return;
            _productSearchText = value;
            Changed();
            SearchProductsCommand.Refresh();
        }
    }
    public SaleProductLookup? SelectedProductMatch
    {
        get => _selectedProductMatch;
        set
        {
            if (_selectedProductMatch == value) return;
            _selectedProductMatch = value;
            Changed();
            AddSelectedProductCommand.Refresh();
        }
    }
    public bool HasMultipleProductMatches => ProductMatches.Count > 1;
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "Card", "Credit", "Mixed"];
    public string BillDiscountText { get => _billDiscountText; set { if (_billDiscountText == value) return; _billDiscountText = value; Changed(); Recalculate(); } }
    public string PaidText { get => _paidText; set { if (_paidText == value) return; _paidText = value; Changed(); RecalculateChange(); } }
    public string MixedCashText { get => _mixedCashText; set { if (_mixedCashText == value) return; _mixedCashText = value; Changed(); RecalculateChange(); } }
    public string MixedCardText { get => _mixedCardText; set { if (_mixedCardText == value) return; _mixedCardText = value; Changed(); RecalculateChange(); } }
    public string SaleNotes { get => _saleNotes; set { _saleNotes = value; Changed(); } }
    public string PaymentMethod { get => _paymentMethod; set { if (_paymentMethod == value) return; _paymentMethod = value; Changed(); RecalculateChange(); } }
    public decimal Subtotal => _totals.Subtotal;
    public decimal DiscountTotal => _totals.Discount;
    public decimal TaxTotal => _totals.Tax;
    public decimal GrandTotal => _totals.GrandTotal;
    public string DiscountError => _discountError;
    public bool CanComplete => CartLines.Count > 0 && _isDiscountValid;
    public string ChangeSummary => _changeSummary;

    public AsyncCommand RemoveProductCommand { get; }
    public AsyncCommand HoldSaleCommand { get; }
    public AsyncCommand ResumeHeldSaleCommand { get; }
    public AsyncCommand ClearCartCommand { get; }
    public AsyncCommand CancelSaleCommand { get; }
    public AsyncCommand NewSaleCommand { get; }
    public AsyncCommand CompleteSaleCommand { get; }
    public AsyncCommand SearchProductsCommand { get; }
    public AsyncCommand AddSelectedProductCommand { get; }

    public PosViewModel(ProductService products, HeldSaleService heldSales, SaleService sales, PosScreenService screen,
        AppUser user, IUserConfirmationService confirmation)
    {
        _products = products;
        _heldSales = heldSales;
        _sales = sales;
        _screen = screen;
        _user = user;
        _confirmation = confirmation;

        SearchProductsCommand = new(SearchProductsCommandAsync,
            () => !string.IsNullOrWhiteSpace(ProductSearchText) && Permissions.Has(_user.Role, "Sales.Create"));
        AddSelectedProductCommand = new(AddSelectedProductCommandAsync,
            () => SelectedProductMatch is not null && Permissions.Has(_user.Role, "Sales.Create"));

        RemoveProductCommand = new(() => { RemoveProduct(SelectedCartLine); return Task.CompletedTask; },
            () => SelectedCartLine is not null);
        HoldSaleCommand = new(HoldSaleCommandAsync,
            () => CartLines.Count > 0 && Permissions.Has(_user.Role, "Sales.Create"));
        ResumeHeldSaleCommand = new(ResumeHeldSaleCommandAsync,
            () => SelectedHeldSale is not null && Permissions.Has(_user.Role, "Sales.Create"));
        ClearCartCommand = new(ClearCartCommandAsync,
            () => CartLines.Count > 0 && Permissions.Has(_user.Role, "Sales.Create"));
        CancelSaleCommand = new(CancelSaleCommandAsync,
            () => (CartLines.Count > 0 || ResumedHeldSaleId is not null) && Permissions.Has(_user.Role, "Sales.Create"));
        NewSaleCommand = new(NewSaleCommandAsync, () => Permissions.Has(_user.Role, "Sales.Create"));
        CompleteSaleCommand = new(CompleteSaleCommandAsync, () => CanComplete && Permissions.Has(_user.Role, "Sales.Create"));

        CartLines.CollectionChanged += CartLines_CollectionChanged;
    }

    public async Task InitializeAsync(string defaultStoreName, CancellationToken ct = default)
    {
        try
        {
            var setup = await _screen.GetSetupAsync(_user.Id, defaultStoreName, ct);
            StoreName = setup.StoreName;
            Customers.Clear();
            foreach (var customer in setup.Customers) Customers.Add(customer);
            SelectedCustomerId = null;
            await RefreshHeldSalesAsync(ct);
        }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Loading the point-of-sale screen")); }
    }

    public void ReportStatus(string message) => StatusMessage = message;

    private void CartLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Recalculate();
        HoldSaleCommand.Refresh(); ClearCartCommand.Refresh(); CancelSaleCommand.Refresh();
        CompleteSaleCommand.Refresh(); RemoveProductCommand.Refresh();
    }

    private async Task RefreshHeldSalesAsync(CancellationToken ct = default)
    {
        var selectedId = SelectedHeldSale?.Id;
        var results = await SearchHeldSalesAsync(ct);
        HeldSales.Clear();
        foreach (var held in results) HeldSales.Add(held);
        SelectedHeldSale = HeldSales.FirstOrDefault(x => x.Id == selectedId);
        ResumeHeldSaleCommand.Refresh();
    }

    private async Task HoldSaleCommandAsync()
    {
        try
        {
            var held = await HoldAsync(SelectedCustomerId, SaleNotes, BillDiscountText);
            ResetCheckout(); await RefreshHeldSalesAsync(); ReportStatus($"Sale held ({held.DisplayLabel}).");
        }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Holding the sale")); }
    }

    private async Task ResumeHeldSaleCommandAsync()
    {
        if (SelectedHeldSale is not { } selected) { ReportStatus("Choose a held sale to resume."); return; }
        try
        {
            var snapshot = await ResumeAsync(selected.Id);
            ResetCheckout(); SaleNotes = snapshot.Sale.Notes ?? "";
            BillDiscountText = snapshot.Sale.BillDiscount.ToString(CultureInfo.CurrentCulture);
            SelectedCustomerId = snapshot.Sale.CustomerId;
            ReportStatus(snapshot.Items.Any(x => x.Quantity > x.AvailableStock)
                ? "Sale resumed. One or more quantities exceed current stock; adjust the cart before completing."
                : "Sale resumed.");
        }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Resuming the held sale")); }
    }

    private Task ClearCartCommandAsync()
    {
        ClearCart(); ResetCheckout(); ReportStatus("Cart cleared."); return Task.CompletedTask;
    }

    private async Task CancelSaleCommandAsync()
    {
        if (CartLines.Count == 0 && ResumedHeldSaleId is null) { ReportStatus("There is no active sale to cancel."); return; }
        var total = GrandTotal;
        var prompt = ResumedHeldSaleId is null
            ? $"Cancel this sale with {CartLines.Count} line(s), total {total:N2}?"
            : $"Cancel this sale with {CartLines.Count} line(s), total {total:N2}, and delete its held-sale record?";
        if (!await _confirmation.ConfirmAsync("Confirm sale cancellation", prompt, ConfirmationKind.Warning)) return;
        try
        {
            await CancelAsync(CartLines.Count, total); ResetCheckout(clearCustomer: true);
            await RefreshHeldSalesAsync(); ReportStatus("Sale cancelled and recorded in the activity log.");
        }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Cancelling the sale")); }
    }

    private async Task NewSaleCommandAsync()
    {
        if (CartLines.Count > 0 && !await _confirmation.ConfirmAsync("New sale", "Discard the current cart and start a new sale?")) return;
        ClearCart(); ResetCheckout(clearCustomer: true); ReportStatus("New sale started.");
    }

    private async Task CompleteSaleCommandAsync()
    {
        PosCheckoutRequest checkout;
        try { checkout = PrepareCheckout(); }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Validating the sale")); return; }
        if (!await _confirmation.ConfirmAsync("Confirm sale", $"Complete sale for {checkout.Totals.GrandTotal:N2}?")) return;
        try
        {
            var sale = await CompleteSaleAsync(checkout, SaleNotes); LastSale = sale;
            ResetCheckout(); await RefreshHeldSalesAsync();
            ReportStatus($"Sale {sale.InvoiceNumber} saved. Change: {SaleCalculator.Change(checkout.PaidAmount, sale.GrandTotal):N2}");
        }
        catch (Exception ex) { ReportStatus(UserErrorHandler.GetMessage(ex, "Completing the sale")); }
    }

    public ObservableCollection<PosCartLine> CartLines { get; } = [];
    public int? ResumedHeldSaleId { get; private set; }

    public Task<IReadOnlyList<SaleProductLookup>> SearchProductsAsync(string query, CancellationToken ct = default) =>
        _products.SearchForSaleAsync(query, _user.Id, ct);

    private async Task SearchProductsCommandAsync()
    {
        var query = ProductSearchText.Trim();
        ProductMatches.Clear();
        SelectedProductMatch = null;
        Changed(nameof(HasMultipleProductMatches));
        if (query.Length == 0) return;

        IReadOnlyList<SaleProductLookup> matches;
        try { matches = await SearchProductsAsync(query); }
        catch (Exception ex)
        {
            ReportStatus(UserErrorHandler.GetMessage(ex, "Searching for a sale product"));
            return;
        }

        foreach (var match in matches) ProductMatches.Add(match);
        Changed(nameof(HasMultipleProductMatches));
        if (matches.Count == 0)
        {
            ReportStatus("No active product matches that barcode, SKU, or name.");
            return;
        }
        if (matches.Count > 1)
        {
            ReportStatus("More than one product matches. Select the correct product by SKU, barcode, price, and stock.");
            return;
        }

        SelectedProductMatch = matches[0];
        await AddSelectedProductCommandAsync();
    }

    private Task AddSelectedProductCommandAsync()
    {
        if (SelectedProductMatch is not { } selected)
        {
            ReportStatus("Select a product to add to the cart.");
            return Task.CompletedTask;
        }

        var error = AddProduct(selected);
        if (error.Length > 0)
        {
            ReportStatus(error);
            return Task.CompletedTask;
        }

        ProductSearchText = "";
        ProductMatches.Clear();
        SelectedProductMatch = null;
        Changed(nameof(HasMultipleProductMatches));
        ReportStatus("");
        return Task.CompletedTask;
    }

    public string AddProduct(SaleProductLookup product)
    {
        if (product.AvailableStock <= 0) return $"{product.ProductName} is out of stock.";
        var existing = CartLines.FirstOrDefault(x => x.ProductId == product.ProductId);
        if (existing is not null)
        {
            if (existing.Quantity + 1 > product.AvailableStock) return $"Only {product.AvailableStock} in stock.";
            existing.Quantity++;
        }
        else
        {
            var line = new PosCartLine(product.ProductId, product.ProductName, 1, product.SalePrice,
                product.TaxRate, product.DiscountRate, product.AvailableStock);
            TrackLine(line); CartLines.Add(line);
        }
        Recalculate();
        return "";
    }

    public void AddHeldSaleItem(HeldSaleProduct item)
    {
        var line = new PosCartLine(item.ProductId, item.ProductName, item.Quantity, item.SalePrice,
            item.TaxRate, item.DiscountRate, item.AvailableStock);
        TrackLine(line); CartLines.Add(line);
        Recalculate();
    }

    private void TrackLine(PosCartLine line) => line.PropertyChanged += CartLineChanged;
    private void CartLineChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    public void RemoveProduct(PosCartLine? line)
    {
        if (line is not null && CartLines.Remove(line)) line.PropertyChanged -= CartLineChanged;
        Recalculate();
    }

    public void ClearCart()
    {
        foreach (var line in CartLines) line.PropertyChanged -= CartLineChanged;
        CartLines.Clear();
        ResumedHeldSaleId = null;
        Recalculate();
    }

    public void ResetCheckout(bool clearCustomer = false)
    {
        SaleNotes = ""; BillDiscountText = "0"; PaidText = "0"; MixedCashText = "0"; MixedCardText = "0";
        if (clearCustomer) SelectedCustomerId = null;
        PaymentMethod = "Cash";
    }

    public Task<IReadOnlyList<HeldSaleSummary>> SearchHeldSalesAsync(CancellationToken ct = default) =>
        _heldSales.SearchAsync(_user.Id, ct);

    public async Task<HeldSaleSummary> HoldAsync(int? customerId, string? notes, string billDiscountText,
        CancellationToken ct = default)
    {
        if (CartLines.Count == 0) throw new PosRuleException("Add a product before holding the sale.");
        if (!decimal.TryParse(billDiscountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var billDiscount) || billDiscount < 0)
            throw new PosRuleException("Enter a valid non-negative bill discount.");
        var totals = CalculateTotals(billDiscountText);
        if (!totals.IsDiscountValid) throw new PosRuleException(totals.DiscountError);
        var held = await _heldSales.HoldAsync(CartLines.Select(x => (x.ProductId, x.Quantity)).ToArray(), _user.Id,
            customerId, notes: notes, replaceHeldSaleId: ResumedHeldSaleId, billDiscount: billDiscount,
            discountRates: CartLines.ToDictionary(x => x.ProductId, x => x.DiscountRate), ct: ct);
        ClearCart();
        return held;
    }

    public async Task<HeldSaleSnapshot> ResumeAsync(int heldSaleId, CancellationToken ct = default)
    {
        var snapshot = await _heldSales.ResumeAsync(heldSaleId, _user.Id, ct);
        ClearCart();
        foreach (var item in snapshot.Items) AddHeldSaleItem(item);
        ResumedHeldSaleId = snapshot.Sale.Id;
        return snapshot;
    }

    public async Task CancelAsync(int itemCount, decimal total, CancellationToken ct = default)
    {
        await _heldSales.CancelAsync(ResumedHeldSaleId, _user.Id, itemCount, total, ct);
        ClearCart();
    }

    public PosCheckoutRequest PrepareCheckout(int? customerId, string paymentMethod, string billDiscountText,
        string paidText, string mixedCashText, string mixedCardText)
    {
        if (CartLines.Count == 0) throw new PosRuleException("Add a product before completing the sale.");
        if (paymentMethod is not ("Cash" or "Card" or "Credit" or "Mixed"))
            throw new PosRuleException("Choose Cash, Card, Credit, or Mixed as the payment method.");
        if (paymentMethod == "Credit" && !Permissions.Has(_user.Role, "Sales.Credit"))
            throw new PosRuleException("Your role cannot create credit sales.");
        if (!decimal.TryParse(billDiscountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var billDiscount) || billDiscount < 0)
            throw new PosRuleException("Enter a valid non-negative bill discount.");
        var totals = CalculateTotals(billDiscountText);
        if (!totals.IsDiscountValid) throw new PosRuleException(totals.DiscountError);
        decimal paid;
        IReadOnlyCollection<(string Method, decimal Amount)>? paymentBreakdown = null;
        if (paymentMethod == "Mixed")
        {
            if (!decimal.TryParse(mixedCashText, NumberStyles.Number, CultureInfo.CurrentCulture, out var cash) || cash < 0
                || !decimal.TryParse(mixedCardText, NumberStyles.Number, CultureInfo.CurrentCulture, out var card) || card < 0)
                throw new PosRuleException("Enter valid non-negative cash and card allocations.");
            paid = cash + card;
            paymentBreakdown = [("Cash", cash), ("Card", card)];
            if (paid != totals.Totals.GrandTotal)
                throw new PosRuleException("Cash and card allocations must add up exactly to the sale total.");
        }
        else if (!decimal.TryParse(paidText, NumberStyles.Number, CultureInfo.CurrentCulture, out paid) || paid < 0)
            throw new PosRuleException("Enter a valid non-negative amount received.");

        if (paymentMethod == "Credit" && customerId is null)
            throw new PosRuleException("Select a customer to make a credit sale.");
        if (paymentMethod != "Credit" && paid < totals.Totals.GrandTotal)
            throw new PosRuleException("Amount received is less than the sale total.");
        if (paymentMethod != "Cash" && paid > totals.Totals.GrandTotal)
            throw new PosRuleException("Overpayment is only accepted for cash sales.");
        return new PosCheckoutRequest(customerId, paymentMethod, billDiscount, paid, totals.Totals, paymentBreakdown);
    }

    public PosCheckoutRequest PrepareCheckout() => PrepareCheckout(SelectedCustomerId, PaymentMethod, BillDiscountText,
        PaidText, MixedCashText, MixedCardText);

    public async Task<Sale> CompleteSaleAsync(PosCheckoutRequest checkout, string? notes, CancellationToken ct = default)
    {
        if (CartLines.Count == 0) throw new PosRuleException("Add a product before completing the sale.");
        var sale = await _sales.CompleteAsync(CartLines.Select(x => (x.ProductId, x.Quantity)).ToArray(), _user.Id,
            checkout.CustomerId, checkout.BillDiscount, checkout.PaidAmount, checkout.PaymentMethod, notes, heldSaleId: ResumedHeldSaleId,
            paymentBreakdown: checkout.PaymentBreakdown,
            lineDiscountRates: CartLines.ToDictionary(x => x.ProductId, x => x.DiscountRate), ct: ct);
        ClearCart();
        return sale;
    }

    public PosCartTotals CalculateTotals(string? billDiscountText)
    {
        var discountValid = decimal.TryParse(billDiscountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var billDiscount)
            && billDiscount >= 0;
        var lines = CartLines.Select(x => new SaleLine(x.Quantity, x.UnitPrice, 0m, x.DiscountRate, x.TaxRate)).ToArray();
        SaleTotals totals;
        try { totals = SaleCalculator.Calculate(lines, discountValid ? billDiscount : 0); }
        catch (ArgumentException)
        {
            discountValid = false;
            totals = SaleCalculator.Calculate(lines);
        }
        return new PosCartTotals(totals, discountValid,
            discountValid ? "" : "Enter a valid discount that does not exceed the remaining subtotal.");
    }

    private void Recalculate()
    {
        var calculation = CalculateTotals(BillDiscountText);
        _totals = calculation.Totals;
        _isDiscountValid = calculation.IsDiscountValid;
        _discountError = calculation.DiscountError;
        Changed(nameof(Subtotal)); Changed(nameof(DiscountTotal)); Changed(nameof(TaxTotal)); Changed(nameof(GrandTotal));
        Changed(nameof(DiscountError)); Changed(nameof(CanComplete));
        CompleteSaleCommand?.Refresh();
        RecalculateChange();
    }

    private void RecalculateChange()
    {
        var paid = PaymentMethod == "Mixed"
            ? (decimal.TryParse(MixedCashText, NumberStyles.Number, CultureInfo.CurrentCulture, out var cash) ? cash : 0)
                + (decimal.TryParse(MixedCardText, NumberStyles.Number, CultureInfo.CurrentCulture, out var card) ? card : 0)
            : decimal.TryParse(PaidText, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) ? amount : 0;
        _changeSummary = PaymentMethod == "Mixed"
            ? $"Remaining: {Math.Max(0, GrandTotal - paid):N2}"
            : $"Change: {(PaymentMethod == "Cash" ? SaleCalculator.Change(paid, GrandTotal) : 0):N2}";
        Changed(nameof(ChangeSummary));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record PosCartTotals(SaleTotals Totals, bool IsDiscountValid, string DiscountError);
public sealed record PosCheckoutRequest(int? CustomerId, string PaymentMethod, decimal BillDiscount, decimal PaidAmount,
    SaleTotals Totals, IReadOnlyCollection<(string Method, decimal Amount)>? PaymentBreakdown);

public sealed class PosCartLine(int productId, string productName, decimal quantity, decimal unitPrice,
    decimal taxRate, decimal discountRate, decimal available) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public int ProductId { get; } = productId;
    public string ProductName { get; } = productName;
    private decimal _quantity = quantity;
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Quantity must be greater than zero.");
            if (value > Available) throw new ArgumentOutOfRangeException(nameof(value), $"Only {Available:N2} units are currently available.");
            if (_quantity == value) return;
            _quantity = value;
            PropertyChanged?.Invoke(this, new(nameof(Quantity)));
            PropertyChanged?.Invoke(this, new(nameof(LineTotal)));
        }
    }
    public decimal UnitPrice { get; } = unitPrice;
    public decimal TaxRate { get; } = taxRate;
    private decimal _discountRate = discountRate;
    public decimal DiscountRate
    {
        get => _discountRate;
        set
        {
            if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value), "Discount must be between 0 and 100 percent.");
            if (_discountRate == value) return;
            _discountRate = value;
            PropertyChanged?.Invoke(this, new(nameof(DiscountRate)));
            PropertyChanged?.Invoke(this, new(nameof(LineTotal)));
        }
    }
    public decimal Available { get; } = available;
    public decimal LineTotal => Quantity * UnitPrice * (1 - DiscountRate / 100m) * (1 + TaxRate / 100m);
}
