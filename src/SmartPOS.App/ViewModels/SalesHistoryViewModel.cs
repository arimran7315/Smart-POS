using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class SalesHistoryViewModel : INotifyPropertyChanged
{
    private readonly SaleService _sales; private readonly ReturnsService _returns; private readonly int _userId; private readonly string _role;
    private readonly IUserConfirmationService _confirmation;
    private Sale? _selectedSale; private SaleItem? _selectedItem; private string _search = "", _returnQuantity = "1", _reason = "Customer return", _voidReason = "", _status = "";
    private DateTime? _fromDate;
    private DateTime? _throughDate;
    private string _statusFilter = "All";
    private int _pageIndex;
    private int _totalCount;
    private const int PageSize = 100;
    public ObservableCollection<Sale> Sales { get; } = [];
    public ObservableCollection<SaleItem> Items { get; } = [];
    public IReadOnlyList<string> StatusOptions { get; } = ["All", "Completed", "Credit", "Returned", "Voided"];
    public Sale? SelectedSale { get => _selectedSale; set { _selectedSale = value; OnChanged(); OnChanged(nameof(CanReprint)); LoadItems(); ReturnCommand?.Refresh(); VoidCommand?.Refresh(); } }
    public bool CanReprint => SelectedSale is { Status: not "Voided" };
    public SaleItem? SelectedItem { get => _selectedItem; set { _selectedItem = value; OnChanged(); ReturnCommand?.Refresh(); } }
    public string Search { get => _search; set { _search = value; OnChanged(); } }
    public DateTime? FromDate { get => _fromDate; set { _fromDate = value; OnChanged(); } }
    public DateTime? ThroughDate { get => _throughDate; set { _throughDate = value; OnChanged(); } }
    public string StatusFilter { get => _statusFilter; set { _statusFilter = value; OnChanged(); } }
    public string PageLabel => _totalCount == 0 ? "No sales · 0 records" : $"Page {_pageIndex + 1} of {PageCount} · {_totalCount} sales";
    public int PageCount => _totalCount == 0 ? 0 : (int)(((long)_totalCount + PageSize - 1) / PageSize);
    public string ReturnQuantity { get => _returnQuantity; set { _returnQuantity = value; OnChanged(); } }
    public string Reason { get => _reason; set { _reason = value; OnChanged(); } }
    public string VoidReason { get => _voidReason; set { _voidReason = value; OnChanged(); VoidCommand?.Refresh(); } }
    public string Status { get => _status; private set { _status = value; OnChanged(); } }
    public void ReportStatus(string status) => Status = status;
    public async Task<SaleReceiptData?> GetReceiptForReprintAsync(int saleId)
    {
        try { return await _sales.GetReceiptForReprintAsync(saleId, _userId); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Preparing receipt reprint"); return null; }
    }
    public async Task<bool> RecordReceiptReprintedAsync(int saleId)
    {
        try
        {
            await _sales.RecordReceiptReprintedAsync(saleId, _userId);
            return true;
        }
        catch (Exception ex)
        {
            var detail = UserErrorHandler.GetMessage(ex, "Recording receipt reprint");
            Status = $"The receipt was sent to the printer, but its activity entry could not be saved. Do not reprint because of this message. {detail}";
            return false;
        }
    }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand PreviousPageCommand { get; }
    public AsyncCommand NextPageCommand { get; }
    public AsyncCommand ReturnCommand { get; }
    public AsyncCommand VoidCommand { get; }
    public SalesHistoryViewModel(SaleService sales, ReturnsService returns, AppUser user, IUserConfirmationService confirmation)
    {
        _sales = sales; _returns = returns; _userId = user.Id; _role = user.Role; _confirmation = confirmation;
        SearchCommand = new(() => LoadAsync(resetPage: true));
        PreviousPageCommand = new(PreviousPageAsync, () => _pageIndex > 0);
        NextPageCommand = new(NextPageAsync, () => _pageIndex + 1 < PageCount);
        ReturnCommand = new(ReturnAsync, () => SelectedSale is not null && SelectedSale.Status != "Voided" && SelectedItem is not null && Permissions.Has(_role, "Sales.Return"));
        VoidCommand = new(VoidAsync, () => SelectedSale is not null && SelectedSale.Status != "Voided"
            && !string.IsNullOrWhiteSpace(VoidReason) && Permissions.Has(_role, "Sales.Void"));
    }
    public async Task InitializeAsync() => await LoadAsync(resetPage: true);
    private async Task LoadAsync(bool resetPage = false)
    {
        try
        {
            if (resetPage) _pageIndex = 0;
            var selectedId = SelectedSale?.Id;
            var from = FromDate is DateTime fromDate ? DateOnly.FromDateTime(fromDate) : (DateOnly?)null;
            var through = ThroughDate is DateTime throughDate ? DateOnly.FromDateTime(throughDate) : (DateOnly?)null;
            var page = await _sales.SearchPageAsync(_userId, Search, _pageIndex, PageSize, from: from, through: through, status: StatusFilter);
            if (page.PageCount > 0 && _pageIndex >= page.PageCount)
            {
                _pageIndex = page.PageCount - 1;
                page = await _sales.SearchPageAsync(_userId, Search, _pageIndex, PageSize, from: from, through: through, status: StatusFilter);
            }
            _totalCount = page.TotalCount;
            OnChanged(nameof(PageLabel)); OnChanged(nameof(PageCount));
            PreviousPageCommand?.Refresh(); NextPageCommand?.Refresh();
            Sales.Clear(); foreach (var sale in page.Items) Sales.Add(sale);
            SelectedSale = Sales.FirstOrDefault(x => x.Id == selectedId) ?? Sales.FirstOrDefault();
            Status = $"Loaded {Sales.Count} of {_totalCount} sale(s).";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading sales history"); }
    }
    private async Task PreviousPageAsync() { if (_pageIndex > 0) { _pageIndex--; await LoadAsync(); } }
    private async Task NextPageAsync() { if (_pageIndex + 1 < PageCount) { _pageIndex++; await LoadAsync(); } }
    private void LoadItems()
    {
        Items.Clear(); SelectedItem = null; if (SelectedSale is not null) foreach (var item in SelectedSale.Items) Items.Add(item);
    }
    private async Task ReturnAsync()
    {
        if (SelectedSale is null || SelectedItem is null) return;
        var sale = SelectedSale;
        var item = SelectedItem;
        if (!await _confirmation.ConfirmAsync("Confirm return",
                $"Process a return for {item.ProductName} on invoice {sale.InvoiceNumber}? Stock and customer balance/refund will be updated.",
                ConfirmationKind.Warning)) return;
        if (!decimal.TryParse(ReturnQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0)
        { Status = "Enter a positive return quantity."; return; }
        try
        {
            var result = await _returns.ReturnAsync(sale.Id, _userId, [(item.Id, quantity)], Reason);
            Status = $"Return {result.ReturnNumber} recorded. Refund/credit value: {result.Total:N2}.";
            ReturnQuantity = "1"; await LoadAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Processing the sale return"); }
    }
    private async Task VoidAsync()
    {
        if (SelectedSale is null) return;
        var sale = SelectedSale;
        if (!await _confirmation.ConfirmAsync("Confirm sale void",
                $"Void invoice {sale.InvoiceNumber} for {sale.GrandTotal:N2}? Stock will be restored and recorded payments reversed in {SmartPosIdentity.Name}. Process any required cash/card refund with the payment provider.",
                ConfirmationKind.Warning)) return;
        try
        {
            var invoice = sale.InvoiceNumber;
            await _sales.VoidAsync(sale.Id, _userId, VoidReason);
            Status = $"Sale {invoice} was voided; stock was restored and recorded payments were reversed.";
            VoidReason = "";
            await LoadAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Voiding the sale"); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
