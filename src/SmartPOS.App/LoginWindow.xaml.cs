using System.Windows;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App;

public partial class LoginWindow : Window
{
    private readonly AuthenticationService _auth; private readonly PosDbContext _db; private readonly bool _setup;
    public AppUser? CurrentUser { get; private set; }
    public LoginWindow(AuthenticationService auth, PosDbContext db)
    {
        InitializeComponent(); _auth = auth; _db = db; _setup = !_db.Users.Any();
        if (_setup) { ModeText.Text = "Create the first administrator account"; SubmitButton.Content = "Create administrator"; DisplayNameLabel.Visibility = DisplayNameBox.Visibility = ConfirmLabel.Visibility = ConfirmBox.Visibility = Visibility.Visible; HintText.Text = "Use a unique password with at least 12 characters. Passwords are stored as salted BCrypt hashes."; }
        else HintText.Text = "Sign in with your SmartPOS account.";
    }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        try
        {
            if (_setup)
            {
                if (PasswordBox.Password != ConfirmBox.Password) throw new InvalidOperationException("Passwords do not match.");
                CurrentUser = await _auth.CreateFirstAdministratorAsync(UsernameBox.Text, DisplayNameBox.Text, PasswordBox.Password);
            }
            else CurrentUser = await _auth.LoginAsync(UsernameBox.Text, PasswordBox.Password);
            if (CurrentUser is null) throw new InvalidOperationException("Username or password is incorrect, or the account is inactive.");
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Sign in", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SubmitButton.IsEnabled = true; }
    }
}
