using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

/// <summary>A single column/point a chart control can render, independent of the source report type.</summary>
public interface IChartColumn
{
    string Label { get; }
    double Magnitude { get; }
    double Scale { get; }
    bool IsNegative { get; }
    string Display { get; }
}

public sealed record DashboardChartPoint(string Label, string Amount, double Fraction, bool IsNegative) : IChartColumn
{
    double IChartColumn.Magnitude => Fraction;
    double IChartColumn.Scale => 1;
    string IChartColumn.Display => Amount;
}

public sealed class DashboardViewModel(DashboardService dashboard, int userId, string role) : INotifyPropertyChanged
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
    public ObservableCollection<DashboardChartPoint> SalesTrend { get; } = [];
    public ObservableCollection<DashboardChartPoint> PurchasesTrend { get; } = [];
    public ObservableCollection<DashboardChartPoint> ProfitTrend { get; } = [];
    public ObservableCollection<DashboardTopProduct> TopProducts { get; } = [];
    public ObservableCollection<DashboardRecentSale> RecentSales { get; } = [];
    public ObservableCollection<DashboardRecentPurchase> RecentPurchases { get; } = [];
    public async Task RefreshAsync(bool showProfit)
    {
        try
        {
            var d = await dashboard.GetTodayAsync(userId); Sales = d.TodaySales?.ToString("N2") ?? "Restricted"; Purchases = d.TodayPurchases?.ToString("N2") ?? "Restricted";
            Profit = showProfit && d.TodayProfit is not null ? d.TodayProfit.Value.ToString("N2") : "Restricted"; Expenses = d.TodayExpenses?.ToString("N2") ?? "Restricted";
            Products = d.ProductCount.ToString("N0"); LowStock = d.LowStockCount.ToString("N0"); Customers = d.CustomerCount.ToString("N0"); Suppliers = d.SupplierCount.ToString("N0");
            var trends = await dashboard.GetLastSevenDaysAsync(userId);
            TopProducts.Clear();
            foreach (var product in await dashboard.GetTopProductsTodayAsync(userId)) TopProducts.Add(product);
            RecentSales.Clear();
            if (Permissions.Has(role, "Sales.View"))
                foreach (var sale in await dashboard.GetRecentSalesAsync(userId)) RecentSales.Add(sale);
            RecentPurchases.Clear();
            if (Permissions.Has(role, "Purchases.Manage") || Permissions.Has(role, "Purchases.Return"))
                foreach (var purchase in await dashboard.GetRecentPurchasesAsync(userId)) RecentPurchases.Add(purchase);
            UpdateChart(SalesTrend, trends.Select(x => (x.Day, x.Sales)).ToArray());
            UpdateChart(PurchasesTrend, trends.Select(x => (x.Day, x.Purchases)).ToArray());
            ProfitTrend.Clear();
            if (showProfit) UpdateChart(ProfitTrend, trends.Where(x => x.Profit.HasValue).Select(x => (x.Day, x.Profit!.Value)).ToArray());
            Status = $"Updated {DateTime.Now:t}. Figures use local calendar day converted from UTC.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading the dashboard"); }
    }
    private static void UpdateChart(ObservableCollection<DashboardChartPoint> target, IReadOnlyList<(DateOnly Day, decimal Amount)> points)
    {
        target.Clear();
        var max = points.Select(x => Math.Abs(x.Amount)).DefaultIfEmpty(0).Max();
        foreach (var point in points)
            target.Add(new DashboardChartPoint(point.Day.ToString("ddd dd", CultureInfo.CurrentCulture), point.Amount.ToString("N0", CultureInfo.CurrentCulture),
                max == 0 ? 0 : (double)(Math.Abs(point.Amount) / max), point.Amount < 0));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}
