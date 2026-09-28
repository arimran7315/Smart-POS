using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class LoginViewModel(AuthenticationService authentication) : INotifyPropertyChanged
{
    private bool _setupRequired;
    private bool _isBusy;
    private bool _canSubmit;
    private string _modeText = "Checking setup…";
    private string _submitText = "Sign in";
    private string _status = "";
    private string _userName = "";
    private string _displayName = "";

    public bool SetupRequired { get => _setupRequired; private set { _setupRequired = value; Changed(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Changed(); Changed(nameof(CanSubmit)); } }
    public bool CanSubmit { get => _canSubmit && !IsBusy; private set { _canSubmit = value; Changed(); } }
    public string ModeText { get => _modeText; private set { _modeText = value; Changed(); } }
    public string SubmitText { get => _submitText; private set { _submitText = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string UserName { get => _userName; set { _userName = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public AppUser? CurrentUser { get; private set; }

    public async Task InitializeAsync(string applicationName, CancellationToken ct = default)
    {
        CanSubmit = false;
        try
        {
            SetupRequired = await authentication.IsFirstAdministratorRequiredAsync(ct);
            ModeText = SetupRequired ? "Create the first administrator account" : "Sign in to continue";
            SubmitText = SetupRequired ? "Create administrator" : "Sign in";
            Status = SetupRequired
                ? $"Use a unique password with at least {PasswordHasher.MinimumPasswordLength} characters. Passwords are stored as salted BCrypt hashes."
                : $"Sign in with your {applicationName} account.";
            CanSubmit = true;
        }
        catch (Exception ex)
        {
            Status = UserErrorHandler.GetMessage(ex, "Checking first-run setup");
        }
    }

    public async Task<bool> SubmitAsync(string password, string confirmation = "", CancellationToken ct = default)
    {
        if (!CanSubmit) return false;
        IsBusy = true;
        Status = "";
        try
        {
            if (SetupRequired)
            {
                if (password.Length < PasswordHasher.MinimumPasswordLength)
                {
                    Status = $"Use a password with at least {PasswordHasher.MinimumPasswordLength} characters.";
                    return false;
                }
                if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                {
                    Status = "The password and confirmation do not match.";
                    return false;
                }
                CurrentUser = await authentication.CreateFirstAdministratorAsync(UserName, DisplayName, password, ct);
            }
            else
            {
                CurrentUser = await authentication.LoginAsync(UserName, password, ct);
                if (CurrentUser is null)
                {
                    Status = "Username or password is incorrect, the account is inactive, or sign-in is temporarily locked.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Status = UserErrorHandler.GetMessage(ex, "Signing in");
            return false;
        }
        finally { IsBusy = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
