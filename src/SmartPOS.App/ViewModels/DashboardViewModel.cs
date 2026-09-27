using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class DashboardViewModel(DashboardService dashboard, int userId) : INotifyPropertyChanged
{
    private string _status = "Loading today's figures…";
    private string _sales = "—", _purchases = "—", _profit = "—", _expenses = "—";
    private string _products = "—", _lowStock = "—", _customers = "—", _suppliers = "—";
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Sales { get => _sales; private set { _sales = value; Changed(); } }
    public string Purchases { get => _purchases; private set { _purchases = value; Changed(); } }
    public string Profit { get => _profit; private set { _profit = value; Changed(); } }
    public string Expenses { get => _expenses; private set { _expenses = value; Changed(); } }
    public string Products { get => _products; private set { _products = value; Changed(); } }
    public string LowStock { get => _lowStock; private set { _lowStock = value; Changed(); } }
    public string Customers { get => _customers; private set { _customers = value; Changed(); } }
    public string Suppliers { get => _suppliers; private set { _suppliers = value; Changed(); } }
    public async Task RefreshAsync(bool showProfit)
    {
        try
        {
            var d = await dashboard.GetTodayAsync(userId); Sales = d.TodaySales.ToString("N2"); Purchases = d.TodayPurchases.ToString("N2");
            Profit = showProfit && d.TodayProfit is not null ? d.TodayProfit.Value.ToString("N2") : "Restricted"; Expenses = d.TodayExpenses.ToString("N2");
            Products = d.ProductCount.ToString("N0"); LowStock = d.LowStockCount.ToString("N0"); Customers = d.CustomerCount.ToString("N0"); Suppliers = d.SupplierCount.ToString("N0");
            Status = $"Updated {DateTime.Now:t}. Figures use local calendar day converted from UTC.";
        }
        catch (Exception ex) { Status = $"Could not load dashboard: {ex.Message}"; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}
