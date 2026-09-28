using System.Windows;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.App.ViewModels;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    public AppUser? CurrentUser { get; private set; }
    public LoginWindow(AuthenticationService auth)
    {
        InitializeComponent(); _viewModel = new LoginViewModel(auth); DataContext = _viewModel;
        Loaded += async (_, _) => await InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync(SmartPosIdentity.Name);
    }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        var success = await _viewModel.SubmitAsync(PasswordBox.Password, ConfirmBox.Password);
        PasswordBox.Clear(); ConfirmBox.Clear();
        if (!success) return;
        CurrentUser = _viewModel.CurrentUser;
        DialogResult = true;
    }
}
