using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class ReportsWindow : Window
{
    private readonly ReportsViewModel _viewModel;
    public ReportsWindow(ReportsService reports, AppUser user)
    {
        InitializeComponent(); Title = $"{Convert.ToString(TryFindResource("AppName")) ?? "SmartPOS"} | Reports";
        _viewModel = new ReportsViewModel(reports, user); DataContext = _viewModel;
        if (!Permissions.Has(user.Role, "Reports.Profit")) ProfitTab.Visibility = Visibility.Collapsed;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }
}
