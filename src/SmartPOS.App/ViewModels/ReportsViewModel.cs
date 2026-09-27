using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    public ObservableCollection<TopProductRow> TopProducts { get; } = [];
    public ObservableCollection<DailyProfitPoint> Profit { get; } = [];
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
            TopProducts.Clear(); foreach (var row in await _reports.GetTopProductsAsync(_user.Id, from, through)) TopProducts.Add(row);
            Profit.Clear();
            if (Permissions.Has(_user.Role, "Reports.Profit")) foreach (var row in await _reports.GetDailyProfitAsync(_user.Id, from, through)) Profit.Add(row);
            Status = $"Report loaded for {from:yyyy-MM-dd} through {through:yyyy-MM-dd}.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
