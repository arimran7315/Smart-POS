using System.Windows;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.App.ViewModels;
using SmartPOS.UseCases.Authentication;

namespace SmartPOS.App;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    private bool _confirmationWasLastFocused;
    public AppUser? CurrentUser { get; private set; }
    public LoginWindow(IAuthenticationService auth)
    {
        InitializeComponent();
        WindowLayout.FitToWorkArea(this);
        _viewModel = new LoginViewModel(auth); DataContext = _viewModel;
        Loaded += async (_, _) => await InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        await _viewModel.InitializeAsync(SmartPosIdentity.Name);
    }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        var password = ShowPasswordCheckBox.IsChecked == true ? PasswordTextBox.Text : PasswordBox.Password;
        var confirmation = ShowPasswordCheckBox.IsChecked == true ? ConfirmTextBox.Text : ConfirmBox.Password;
        var success = await _viewModel.SubmitAsync(password, confirmation);
        ClearPasswordFields();
        if (!success) return;
        CurrentUser = _viewModel.CurrentUser;
        DialogResult = true;
    }

    private void ShowPassword_Changed(object sender, RoutedEventArgs e)
    {
        var confirmationHasFocus = _confirmationWasLastFocused;
        if (ShowPasswordCheckBox.IsChecked == true)
        {
            PasswordTextBox.Text = PasswordBox.Password;
            ConfirmTextBox.Text = ConfirmBox.Password;
            PasswordBox.Clear();
            ConfirmBox.Clear();
            PasswordBox.Visibility = Visibility.Collapsed;
            ConfirmBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;
            ConfirmTextBox.Visibility = Visibility.Visible;
            if (confirmationHasFocus)
            {
                ConfirmTextBox.Focus();
                ConfirmTextBox.CaretIndex = ConfirmTextBox.Text.Length;
            }
            else
            {
                PasswordTextBox.Focus();
                PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            }
        }
        else
        {
            PasswordBox.Password = PasswordTextBox.Text;
            ConfirmBox.Password = ConfirmTextBox.Text;
            PasswordTextBox.Clear();
            ConfirmTextBox.Clear();
            PasswordTextBox.Visibility = Visibility.Collapsed;
            ConfirmTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            ConfirmBox.Visibility = Visibility.Visible;
            (confirmationHasFocus ? ConfirmBox : PasswordBox).Focus();
        }
    }

    private void PasswordField_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        _confirmationWasLastFocused = ReferenceEquals(sender, ConfirmBox) || ReferenceEquals(sender, ConfirmTextBox);

    private void ClearPasswordFields()
    {
        PasswordBox.Clear();
        ConfirmBox.Clear();
        PasswordTextBox.Clear();
        ConfirmTextBox.Clear();
    }
}
