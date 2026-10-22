using System.Windows;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class ShiftWindow : Window
{
    private readonly ShiftViewModel _viewModel;
    public ShiftWindow(ShiftService shifts, AppUser user)
    {
        InitializeComponent(); WindowLayout.FitToWorkArea(this); Title = $"{SmartPosIdentity.Name} | Shift and cash drawer";
        _viewModel = new ShiftViewModel(shifts, user, new WpfUserConfirmationService(this));
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
}
