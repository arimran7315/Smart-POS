using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using SmartPOS.Infrastructure.Security;

namespace SmartPOS.App.Views;

public partial class BackOfficeWindow : Window
{
    private readonly BackOfficeViewModel _viewModel;
    private readonly PosDbContext _db;
    private readonly AppUser _user;
    private readonly SettingsService _settings;
    private readonly DatabaseBackupService _backup;
    private readonly UserManagementViewModel _userViewModel;
    public BackOfficeWindow(PosDbContext db, AppUser user)
    {
        InitializeComponent(); _db = db; _user = user; _settings = new SettingsService(db); _backup = new DatabaseBackupService(db);
        Title = $"{Convert.ToString(TryFindResource("AppName")) ?? "SmartPOS"} | Business operations";
        _viewModel = new BackOfficeViewModel(new CustomerService(db), new SupplierService(db), new ExpenseService(db), new PaymentService(db), db, user);
        DataContext = _viewModel;
        _userViewModel = new UserManagementViewModel(new UserManagementService(db, new PasswordHasher()), user);
        UsersTab.DataContext = _userViewModel;
        CustomersTab.Visibility = Permissions.Has(user.Role, "Customers.Manage") ? Visibility.Visible : Visibility.Collapsed;
        SuppliersTab.Visibility = Permissions.Has(user.Role, "Suppliers.Manage") ? Visibility.Visible : Visibility.Collapsed;
        ExpensesTab.Visibility = Permissions.Has(user.Role, "Expenses.Manage") ? Visibility.Visible : Visibility.Collapsed;
        CreditTab.Visibility = Permissions.Has(user.Role, "Payments.Receive") ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = Permissions.Has(user.Role, "Settings.View") || Permissions.Has(user.Role, "Database.Backup") ? Visibility.Visible : Visibility.Collapsed;
        AuditTab.Visibility = Permissions.Has(user.Role, "Audit.View") ? Visibility.Visible : Visibility.Collapsed;
        UsersTab.Visibility = Permissions.Has(user.Role, "Users.Manage") ? Visibility.Visible : Visibility.Collapsed;
        SaveSettingButton.Visibility = NewSettingButton.Visibility = Permissions.Has(user.Role, "Settings.Manage") ? Visibility.Visible : Visibility.Collapsed;
        BackupButton.Visibility = RestoreButton.Visibility = Permissions.Has(user.Role, "Database.Backup") ? Visibility.Visible : Visibility.Collapsed;
        MainTabs.SelectedItem = new[] { CustomersTab, SuppliersTab, ExpensesTab, SettingsTab, UsersTab, AuditTab, CreditTab }.FirstOrDefault(x => x.Visibility == Visibility.Visible);
        Loaded += async (_, _) => { await _viewModel.InitializeAsync(); if (SettingsTab.Visibility == Visibility.Visible) await LoadSettingsAsync(); if (AuditTab.Visibility == Visibility.Visible) AuditGrid.ItemsSource = await new AuditService(_db).RecentAsync(_user.Id); if (UsersTab.Visibility == Visibility.Visible) await _userViewModel.InitializeAsync(); };
    }
    private async Task LoadSettingsAsync() => SettingsGrid.ItemsSource = (await _settings.GetAllAsync(_user.Id)).OrderBy(x => x.Key).Select(x => new AppSetting { Key = x.Key, Value = x.Value }).ToList();
    private void SettingsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SettingsGrid.SelectedItem is AppSetting setting) { SettingKeyBox.Text = setting.Key; SettingValueBox.Text = setting.Value; }
    }
    private void NewSetting_Click(object sender, RoutedEventArgs e) { SettingsGrid.SelectedItem = null; SettingKeyBox.Clear(); SettingValueBox.Clear(); }
    private void UserPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _userViewModel.Password = UserPasswordBox.Password;
    private void NewUser_Click(object sender, RoutedEventArgs e) => UserPasswordBox.Clear();
    private async void SaveUser_Click(object sender, RoutedEventArgs e)
    {
        if (await _userViewModel.SaveAsync()) UserPasswordBox.Clear();
        else MessageBox.Show("The account was not saved. Check the account status message.", "User management", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private async void SaveSetting_Click(object sender, RoutedEventArgs e)
    {
        try { await _settings.SetAsync(SettingKeyBox.Text, SettingValueBox.Text, _user.Id); await LoadSettingsAsync(); MessageBox.Show("Setting saved.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite backup (*.db)|*.db|All files (*.*)|*.*", FileName = $"smartpos-{DateTime.Now:yyyyMMdd-HHmmss}.db", AddExtension = true, DefaultExt = ".db" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _backup.CreateBackupAsync(dialog.FileName, _user.Id);
            _db.AuditEntries.Add(new AuditEntry { UserId = _user.Id, Action = "Backup", EntityName = "Database", EntityId = null, Description = "Created a database backup." });
            await _db.SaveChangesAsync(); MessageBox.Show("Database backup created.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SQLite backup (*.db;*.bak)|*.db;*.bak|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show("Restore this database backup? A safety copy of the current database will be created first. Restart SmartPOS after the restore.", "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _backup.RestoreAsync(dialog.FileName, _user.Id);
            var actorExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == _user.Id);
            _db.AuditEntries.Add(new AuditEntry { UserId = actorExists ? _user.Id : null, Action = "Restore", EntityName = "Database", Description = "Restored a database backup; automatic safety copy was created." });
            await _db.SaveChangesAsync(); MessageBox.Show("Backup restored. Close and restart SmartPOS to refresh the active session.", "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
