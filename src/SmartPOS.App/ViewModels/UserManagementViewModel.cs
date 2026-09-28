using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.App.ViewModels;

public sealed class UserManagementViewModel : INotifyPropertyChanged
{
    private readonly UserManagementService _users; private readonly int _actorId;
    private UserAccountSummary? _selectedUser; private string _userName = "", _displayName = "", _password = "", _role = "Cashier", _status = "";
    public ObservableCollection<UserAccountSummary> Users { get; } = [];
    public IReadOnlyList<string> Roles { get; } = ["Administrator", "Manager", "Cashier", "Inventory Manager"];
    public UserAccountSummary? SelectedUser { get => _selectedUser; set { _selectedUser = value; Changed(); SaveCommand?.Refresh(); DeactivateCommand?.Refresh(); if (value is not null) { UserName = value.UserName; DisplayName = value.DisplayName; Role = value.Role; Password = ""; } } }
    public string UserName { get => _userName; set { _userName = value; Changed(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); } }
    public string Password { get => _password; set { _password = value; Changed(); } }
    public string Role { get => _role; set { _role = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeactivateCommand { get; }
    public UserManagementViewModel(UserManagementService users, AppUser actor)
    {
        _users = users; _actorId = actor.Id;
        RefreshCommand = new(LoadAsync); NewCommand = new(() => { SelectedUser = null; UserName = ""; DisplayName = ""; Password = ""; Role = "Cashier"; Status = $"Create a user with a unique username and a password of at least {PasswordHasher.MinimumPasswordLength} characters."; return Task.CompletedTask; });
        SaveCommand = new(async () => { await SaveAsync(); }); DeactivateCommand = new(DeactivateAsync, () => SelectedUser is { Id: > 0, IsActive: true } && SelectedUser.Id != _actorId);
    }
    public async Task InitializeAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        try { Users.Clear(); foreach (var item in await _users.SearchAsync(_actorId)) Users.Add(item); Status = $"Loaded {Users.Count} account(s)."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading user accounts"); }
    }
    public async Task<bool> SaveAsync()
    {
        if (SelectedUser is null && string.IsNullOrWhiteSpace(Password))
        {
            Status = "Enter a password for the new account.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(Password) && Password.Length < PasswordHasher.MinimumPasswordLength)
        {
            Status = $"Use a password with at least {PasswordHasher.MinimumPasswordLength} characters.";
            return false;
        }
        try
        {
            AppUser saved;
            if (SelectedUser is null) saved = await _users.CreateAsync(UserName, DisplayName, Password, Role, _actorId);
            else saved = await _users.UpdateAsync(SelectedUser.Id, DisplayName, Role, Password, _actorId);
            Status = $"Saved account {saved.UserName}."; Password = ""; await LoadAsync(); SelectedUser = Users.FirstOrDefault(x => x.Id == saved.Id); return true;
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving the user account"); return false; }
    }
    private async Task DeactivateAsync()
    {
        try { var id = SelectedUser!.Id; await _users.DeactivateAsync(id, _actorId); Status = "Account deactivated. Historical records remain intact."; SelectedUser = null; await LoadAsync(); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Deactivating the user account"); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
