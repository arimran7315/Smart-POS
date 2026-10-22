using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class QuoteLineRow(int productId, string name, decimal quantity, decimal unitPrice, decimal taxRate, decimal discountRate)
{
    public int ProductId { get; } = productId;
    public string ProductName { get; } = name;
    public decimal Quantity { get; } = quantity;
    public decimal UnitPrice { get; } = unitPrice;
    public decimal TaxRate { get; } = taxRate;
    public decimal DiscountRate { get; } = discountRate;
    public decimal Discount => decimal.Round(Quantity * UnitPrice * DiscountRate / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal Tax => decimal.Round((Quantity * UnitPrice - Discount) * TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
    public decimal Total => decimal.Round(Quantity * UnitPrice - Discount + Tax, 2, MidpointRounding.AwayFromZero);
}

public sealed class QuotationsViewModel : INotifyPropertyChanged
{
    private readonly QuotationService _quotes; private readonly ProductService _products; private readonly CustomerService _customers;
    private readonly SaleService _sales; private readonly IUserConfirmationService _confirmation; private readonly int _userId;
    private Product? _selectedProduct; private QuoteLineRow? _selectedLine; private QuotationSummary? _selectedQuotation;
    private string _quantity = "1", _discountRate = "0", _billDiscount = "0", _notes = "", _status = "";
    private int? _selectedCustomerId; private DateTime? _validUntil = DateTime.Today.AddDays(7);
    private string _search = "", _statusFilter = "All", _convertPaymentMethod = "Cash", _convertPaidText = "";

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<QuoteLineRow> Lines { get; } = [];
    public ObservableCollection<QuotationSummary> Quotations { get; } = [];
    public ObservableCollection<QuotationItemView> QuotationItems { get; } = [];
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "Card", "Credit"];
    public IReadOnlyList<string> StatusOptions { get; } = ["All", "Open", "Converted", "Cancelled"];

    public Product? SelectedProduct { get => _selectedProduct; set { _selectedProduct = value; Changed(); AddLineCommand?.Refresh(); } }
    public QuoteLineRow? SelectedLine { get => _selectedLine; set { _selectedLine = value; Changed(); RemoveLineCommand?.Refresh(); } }
    public QuotationSummary? SelectedQuotation { get => _selectedQuotation; set { _selectedQuotation = value; Changed(); ConvertCommand?.Refresh(); CancelQuoteCommand?.Refresh(); _ = LoadItemsAsync(); } }
    public string Quantity { get => _quantity; set { _quantity = value; Changed(); } }
    public string DiscountRate { get => _discountRate; set { _discountRate = value; Changed(); } }
    public string BillDiscount { get => _billDiscount; set { _billDiscount = value; Changed(); } }
    public string Notes { get => _notes; set { _notes = value; Changed(); } }
    public int? SelectedCustomerId { get => _selectedCustomerId; set { _selectedCustomerId = value; Changed(); } }
    public DateTime? ValidUntil { get => _validUntil; set { _validUntil = value; Changed(); } }
    public string Search { get => _search; set { _search = value; Changed(); } }
    public string StatusFilter { get => _statusFilter; set { _statusFilter = value; Changed(); } }
    public string ConvertPaymentMethod { get => _convertPaymentMethod; set { _convertPaymentMethod = value; Changed(); } }
    public string ConvertPaidText { get => _convertPaidText; set { _convertPaidText = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public decimal Total => Lines.Sum(x => x.Total);

    public AsyncCommand AddLineCommand { get; }
    public AsyncCommand RemoveLineCommand { get; }
    public AsyncCommand SaveQuoteCommand { get; }
    public AsyncCommand NewQuoteCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand ConvertCommand { get; }
    public AsyncCommand CancelQuoteCommand { get; }

    public QuotationsViewModel(QuotationService quotes, ProductService products, CustomerService customers, SaleService sales, AppUser user, IUserConfirmationService confirmation)
    {
        _quotes = quotes; _products = products; _customers = customers; _sales = sales; _userId = user.Id; _confirmation = confirmation;
        AddLineCommand = new(AddLine, () => SelectedProduct is not null);
        RemoveLineCommand = new(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); SelectedLine = null; Changed(nameof(Total)); SaveQuoteCommand?.Refresh(); return Task.CompletedTask; }, () => SelectedLine is not null);
        SaveQuoteCommand = new(SaveQuoteAsync, () => Lines.Count > 0);
        NewQuoteCommand = new(() => { Lines.Clear(); SelectedCustomerId = null; BillDiscount = "0"; Notes = ""; ValidUntil = DateTime.Today.AddDays(7); Changed(nameof(Total)); SaveQuoteCommand?.Refresh(); Status = "Started a new quotation."; return Task.CompletedTask; });
        SearchCommand = new(LoadQuotationsAsync);
        ConvertCommand = new(ConvertAsync, () => SelectedQuotation is { Status: "Open" });
        CancelQuoteCommand = new(CancelAsync, () => SelectedQuotation is { Status: "Open" });
    }

    public async Task InitializeAsync()
    {
        try
        {
            Products.Clear(); foreach (var p in await _products.SearchAsync("", _userId, take: 500)) Products.Add(p);
            Customers.Clear(); foreach (var c in await _customers.SearchAsync("", _userId)) Customers.Add(c);
            await LoadQuotationsAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading quotation data"); }
    }

    private async Task LoadQuotationsAsync()
    {
        try
        {
            Quotations.Clear();
            foreach (var q in await _quotes.SearchAsync(_userId, Search, StatusFilter)) Quotations.Add(q);
            Status = $"Loaded {Quotations.Count} quotation(s).";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading quotations"); }
    }

    private async Task LoadItemsAsync()
    {
        QuotationItems.Clear();
        if (SelectedQuotation is null) return;
        try { foreach (var item in await _quotes.GetItemsAsync(SelectedQuotation.Id, _userId)) QuotationItems.Add(item); ConvertPaidText = SelectedQuotation.GrandTotal.ToString(CultureInfo.CurrentCulture); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading quotation items"); }
    }

    private Task AddLine()
    {
        if (SelectedProduct is not { } product) return Task.CompletedTask;
        if (!decimal.TryParse(Quantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0) { Status = "Enter a positive quantity."; return Task.CompletedTask; }
        if (!decimal.TryParse(DiscountRate, NumberStyles.Number, CultureInfo.CurrentCulture, out var discount) || discount is < 0 or > 100) { Status = "Discount rate must be between 0 and 100 percent."; return Task.CompletedTask; }
        var existing = Lines.FirstOrDefault(x => x.ProductId == product.Id && x.DiscountRate == discount);
        if (existing is not null) { Lines.Remove(existing); Lines.Add(new QuoteLineRow(product.Id, product.Name, existing.Quantity + quantity, product.SalePrice, product.TaxRate, discount)); }
        else Lines.Add(new QuoteLineRow(product.Id, product.Name, quantity, product.SalePrice, product.TaxRate, discount));
        Quantity = "1"; DiscountRate = "0"; Changed(nameof(Total)); SaveQuoteCommand.Refresh(); Status = "Item added to quotation.";
        return Task.CompletedTask;
    }

    private async Task SaveQuoteAsync()
    {
        if (Lines.Count == 0) { Status = "Add at least one item."; return; }
        if (!decimal.TryParse(BillDiscount, NumberStyles.Number, CultureInfo.CurrentCulture, out var billDiscount) || billDiscount < 0) { Status = "Enter a valid bill discount."; return; }
        try
        {
            var inputs = Lines.Select(x => new QuotationLineInput(x.ProductId, x.Quantity, x.DiscountRate)).ToArray();
            var validUntil = ValidUntil is null ? (DateOnly?)null : DateOnly.FromDateTime(ValidUntil.Value);
            var quote = await _quotes.CreateAsync(SelectedCustomerId, inputs, billDiscount, Notes, validUntil, _userId);
            Status = $"Saved quotation {quote.QuoteNumber} for {quote.GrandTotal:N2}.";
            Lines.Clear(); Changed(nameof(Total)); SaveQuoteCommand.Refresh();
            await LoadQuotationsAsync();
            SelectedQuotation = Quotations.FirstOrDefault(x => x.Id == quote.Id);
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving the quotation"); }
    }

    private async Task ConvertAsync()
    {
        if (SelectedQuotation is not { Status: "Open" } quote) return;
        if (!decimal.TryParse(ConvertPaidText, NumberStyles.Number, CultureInfo.CurrentCulture, out var paid) || paid < 0) { Status = "Enter the amount paid."; return; }
        if (!await _confirmation.ConfirmAsync("Convert quotation",
                $"Convert quotation {quote.QuoteNumber} into a {ConvertPaymentMethod.ToLowerInvariant()} sale for {quote.GrandTotal:N2}? Stock will be deducted.", ConfirmationKind.Question)) return;
        try
        {
            var sale = await _quotes.ConvertAsync(quote.Id, paid, ConvertPaymentMethod, _userId, _sales);
            Status = $"Quotation {quote.QuoteNumber} converted to sale {sale.InvoiceNumber}.";
            await LoadQuotationsAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Converting the quotation"); }
    }

    private async Task CancelAsync()
    {
        if (SelectedQuotation is not { Status: "Open" } quote) return;
        if (!await _confirmation.ConfirmAsync("Cancel quotation", $"Cancel quotation {quote.QuoteNumber}? This cannot be undone.", ConfirmationKind.Warning)) return;
        try { await _quotes.CancelAsync(quote.Id, _userId); Status = $"Quotation {quote.QuoteNumber} cancelled."; await LoadQuotationsAsync(); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Cancelling the quotation"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
