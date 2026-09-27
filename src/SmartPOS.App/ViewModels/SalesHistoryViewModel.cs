using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class SalesHistoryViewModel : INotifyPropertyChanged
{
    private readonly SaleService _sales; private readonly ReturnsService _returns; private readonly int _userId; private readonly string _role;
    private Sale? _selectedSale; private SaleItem? _selectedItem; private string _search = "", _returnQuantity = "1", _reason = "Customer return", _status = "";
    public ObservableCollection<Sale> Sales { get; } = [];
    public ObservableCollection<SaleItem> Items { get; } = [];
    public Sale? SelectedSale { get => _selectedSale; set { _selectedSale = value; OnChanged(); LoadItems(); ReturnCommand?.Refresh(); } }
    public SaleItem? SelectedItem { get => _selectedItem; set { _selectedItem = value; OnChanged(); } }
    public string Search { get => _search; set { _search = value; OnChanged(); } }
    public string ReturnQuantity { get => _returnQuantity; set { _returnQuantity = value; OnChanged(); } }
    public string Reason { get => _reason; set { _reason = value; OnChanged(); } }
    public string Status { get => _status; private set { _status = value; OnChanged(); } }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand ReturnCommand { get; }
    public SalesHistoryViewModel(SaleService sales, ReturnsService returns, AppUser user)
    {
        _sales = sales; _returns = returns; _userId = user.Id; _role = user.Role;
        SearchCommand = new(LoadAsync);
        ReturnCommand = new(ReturnAsync, () => SelectedSale is not null && SelectedItem is not null && Permissions.Has(_role, "Sales.Return"));
    }
    public async Task InitializeAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            var selectedId = SelectedSale?.Id;
            Sales.Clear(); foreach (var sale in await _sales.SearchAsync(_userId, Search)) Sales.Add(sale);
            SelectedSale = Sales.FirstOrDefault(x => x.Id == selectedId) ?? Sales.FirstOrDefault();
            Status = $"Loaded {Sales.Count} sale(s).";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private void LoadItems()
    {
        Items.Clear(); SelectedItem = null; if (SelectedSale is not null) foreach (var item in SelectedSale.Items) Items.Add(item);
    }
    private async Task ReturnAsync()
    {
        if (!decimal.TryParse(ReturnQuantity, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity <= 0)
        { Status = "Enter a positive return quantity."; return; }
        try
        {
            var result = await _returns.ReturnAsync(SelectedSale!.Id, _userId, [(SelectedItem!.Id, quantity)], Reason);
            Status = $"Return {result.ReturnNumber} recorded. Refund/credit value: {result.Total:N2}.";
            ReturnQuantity = "1"; await LoadAsync();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
