using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class ReportsViewModel : INotifyPropertyChanged
{
    private readonly ReportsService _reports;
    private readonly AppUser _user;
    private string _from = DateOnly.FromDateTime(DateTime.Today.AddDays(-6)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _through = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string _status = "";
    public ObservableCollection<DailySalesPoint> Sales { get; } = [];
    public ObservableCollection<ReportBar> SalesChart { get; } = [];
    public ObservableCollection<ReportBar> TopProductsChart { get; } = [];
    public ObservableCollection<ReportBar> ProfitChart { get; } = [];
    public ObservableCollection<ReportBar> PurchasesChart { get; } = [];
    public ObservableCollection<ReportBar> ExpensesChart { get; } = [];
    public ObservableCollection<ReportBar> LowStockChart { get; } = [];
    public ObservableCollection<ReportBar> SalesCashierChart { get; } = [];
    public ObservableCollection<ReportBar> SupplierPurchasesChart { get; } = [];
    public ObservableCollection<ReportBar> TaxChart { get; } = [];
    private double _salesChartMaximum = 1;
    private double _topProductsChartMaximum = 1;
    private double _profitChartMaximum = 1;
    private double _purchasesChartMaximum = 1;
    public double SalesChartMaximum { get => _salesChartMaximum; private set { _salesChartMaximum = value; Changed(); } }
    public double TopProductsChartMaximum { get => _topProductsChartMaximum; private set { _topProductsChartMaximum = value; Changed(); } }
    public double ProfitChartMaximum { get => _profitChartMaximum; private set { _profitChartMaximum = value; Changed(); } }
    public double PurchasesChartMaximum { get => _purchasesChartMaximum; private set { _purchasesChartMaximum = value; Changed(); } }
    public ObservableCollection<TopProductRow> TopProducts { get; } = [];
    public ObservableCollection<DailyProfitPoint> Profit { get; } = [];
    public ObservableCollection<CashierSalesRow> SalesByCashier { get; } = [];
    public ObservableCollection<CustomerSalesRow> SalesByCustomer { get; } = [];
    public ObservableCollection<PurchaseDayPoint> PurchasesByDay { get; } = [];
    public ObservableCollection<SupplierPurchaseRow> PurchasesBySupplier { get; } = [];
    public ObservableCollection<ExpenseReportRow> Expenses { get; } = [];
    public ObservableCollection<PaymentMethodRow> Payments { get; } = [];
    public ObservableCollection<ReturnReportRow> Returns { get; } = [];
    public ObservableCollection<TaxDayPoint> Tax { get; } = [];
    public ObservableCollection<StockMovementReportRow> StockMovements { get; } = [];
    public ObservableCollection<InventoryValuationRow> InventoryValuation { get; } = [];
    public ObservableCollection<LowStockReportRow> LowStock { get; } = [];
    public ObservableCollection<CustomerLedgerRow> CustomerLedger { get; } = [];
    public ObservableCollection<SupplierLedgerRow> SupplierLedger { get; } = [];
    public string From { get => _from; set { _from = value; Changed(); } }
    public string Through { get => _through; set { _through = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public AsyncCommand RefreshCommand { get; }
    public ReportsViewModel(ReportsService reports, AppUser user)
    {
        _reports = reports; _user = user; RefreshCommand = new(RefreshAsync);
    }
    public async Task RefreshAsync()
    {
        if (!DateOnly.TryParseExact(From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(Through, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through))
        { Status = "Dates must use YYYY-MM-DD format."; return; }
        try
        {
            Sales.Clear(); foreach (var row in await _reports.GetDailySalesAsync(_user.Id, from, through)) Sales.Add(row);
            SalesChartMaximum = SetChart(SalesChart, Sales.Select(x => (x.Day.ToString("MM-dd", CultureInfo.InvariantCulture), x.SalesTotal)));
            TopProducts.Clear(); foreach (var row in await _reports.GetTopProductsAsync(_user.Id, from, through)) TopProducts.Add(row);
            TopProductsChartMaximum = SetChart(TopProductsChart, TopProducts.Select(x => (x.ProductName, x.Revenue)));
            SalesByCashier.Clear(); foreach (var row in await _reports.GetSalesByCashierAsync(_user.Id, from, through)) SalesByCashier.Add(row);
            SetChart(SalesCashierChart, SalesByCashier.Select(x => (x.CashierName, x.Sales)));
            SalesByCustomer.Clear(); foreach (var row in await _reports.GetSalesByCustomerAsync(_user.Id, from, through)) SalesByCustomer.Add(row);
            PurchasesByDay.Clear(); foreach (var row in await _reports.GetPurchasesByDayAsync(_user.Id, from, through)) PurchasesByDay.Add(row);
            PurchasesChartMaximum = SetChart(PurchasesChart, PurchasesByDay.Select(x => (x.Day.ToString("MM-dd", CultureInfo.InvariantCulture), x.NetPurchases)));
            PurchasesBySupplier.Clear(); foreach (var row in await _reports.GetPurchasesBySupplierAsync(_user.Id, from, through)) PurchasesBySupplier.Add(row);
            SetChart(SupplierPurchasesChart, PurchasesBySupplier.Select(x => (x.SupplierName, x.Purchases)));
            Expenses.Clear(); foreach (var row in await _reports.GetExpensesAsync(_user.Id, from, through)) Expenses.Add(row);
            SetChart(ExpensesChart, Expenses.GroupBy(x => x.Category)
                .Select(group => (group.Key, group.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Item2).Take(10));
            Payments.Clear(); foreach (var row in await _reports.GetPaymentsAsync(_user.Id, from, through)) Payments.Add(row);
            Returns.Clear(); foreach (var row in await _reports.GetReturnsAsync(_user.Id, from, through)) Returns.Add(row);
            Tax.Clear(); foreach (var row in await _reports.GetTaxByDayAsync(_user.Id, from, through)) Tax.Add(row);
            SetChart(TaxChart, Tax.Select(x => (x.Day.ToString("MM-dd", CultureInfo.InvariantCulture), x.NetTax)));
            StockMovements.Clear(); foreach (var row in await _reports.GetStockMovementsAsync(_user.Id, from, through)) StockMovements.Add(row);
            InventoryValuation.Clear(); foreach (var row in await _reports.GetInventoryValuationAsync(_user.Id)) InventoryValuation.Add(row);
            LowStock.Clear(); foreach (var row in await _reports.GetLowStockAsync(_user.Id)) LowStock.Add(row);
            SetChart(LowStockChart, LowStock.OrderByDescending(x => x.ReorderQuantity).Take(10)
                .Select(x => ($"{x.ProductName} ({x.Unit})", x.ReorderQuantity)));
            CustomerLedger.Clear(); foreach (var row in await _reports.GetCustomerLedgerAsync(_user.Id, from, through)) CustomerLedger.Add(row);
            SupplierLedger.Clear(); foreach (var row in await _reports.GetSupplierLedgerAsync(_user.Id, from, through)) SupplierLedger.Add(row);
            Profit.Clear();
            if (Permissions.Has(_user.Role, "Reports.Profit")) foreach (var row in await _reports.GetDailyProfitAsync(_user.Id, from, through)) Profit.Add(row);
            ProfitChartMaximum = SetChart(ProfitChart, Profit.Select(x => (x.Day.ToString("MM-dd", CultureInfo.InvariantCulture), x.Profit)));
            Status = $"Report loaded for {from:yyyy-MM-dd} through {through:yyyy-MM-dd}.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading reports"); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    private static double SetChart(ObservableCollection<ReportBar> target, IEnumerable<(string Label, decimal Value)> source)
    {
        target.Clear();
        var rows = source.ToArray();
        var maximum = Math.Max(1, rows.Select(x => (double)Math.Abs(x.Value)).DefaultIfEmpty(0).Max());
        foreach (var row in rows)
            target.Add(new ReportBar(row.Label, (double)Math.Abs(row.Value), maximum, row.Value.ToString("N2", CultureInfo.CurrentCulture)));
        return maximum;
    }
}

public sealed record ReportBar(string Label, double Value, double Maximum, string DisplayValue);
