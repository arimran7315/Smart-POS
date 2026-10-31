using System.Windows;
using System.IO;
using SmartPOS.App.ViewModels;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;
using Microsoft.Win32;

namespace SmartPOS.App.Views;

public partial class BackOfficeWindow : Window
{
    private readonly BackOfficeViewModel _viewModel;
    private readonly AppUser _user;
    private readonly DatabaseBackupService _backup;
    private readonly UserManagementViewModel _userViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    public BackOfficeWindow(AppUser user, CustomerService customers, SupplierService suppliers, ExpenseService expenses,
        PaymentService payments, CustomerAccountPaymentService customerAccountPayments, SupplierPaymentService supplierPayments,
        BackOfficeQueryService queries, SettingsService settings, DatabaseBackupService backup, AuditService audit,
        UserManagementService userManagement)
    {
        InitializeComponent(); WindowLayout.FitToWorkArea(this); _user = user; _backup = backup;
        Title = $"{SmartPosIdentity.Name} | Business operations";
        _viewModel = new BackOfficeViewModel(customers, suppliers, expenses, payments, customerAccountPayments, supplierPayments, queries, audit, user, new WpfUserConfirmationService(this));
        DataContext = _viewModel;
        _userViewModel = new UserManagementViewModel(userManagement, user);
        _userViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UserManagementViewModel.Password)
                && string.IsNullOrEmpty(_userViewModel.Password) && UserPasswordBox.Password.Length > 0)
                UserPasswordBox.Clear();
        };
        UsersTab.DataContext = _userViewModel;
        _settingsViewModel = new SettingsViewModel(settings, user, backup.SupportsCurrentProvider);
        _settingsViewModel.SettingSaved += (_, setting) => ApplyRuntimeSetting(setting);
        SettingsTab.DataContext = _settingsViewModel;
        var canViewCustomers = Permissions.Has(user.Role, "Customers.View") || Permissions.Has(user.Role, "Customers.Manage");
        var canManageCustomers = Permissions.Has(user.Role, "Customers.Manage");
        CustomersTab.Visibility = canViewCustomers ? Visibility.Visible : Visibility.Collapsed;
        CustomerEditor.IsEnabled = canManageCustomers;
        NewCustomerButton.Visibility = SaveCustomerButton.Visibility = canManageCustomers ? Visibility.Visible : Visibility.Collapsed;
        CustomerAccountPaymentPanel.Visibility = Permissions.Has(user.Role, "Payments.Receive") ? Visibility.Visible : Visibility.Collapsed;
        SuppliersTab.Visibility = Permissions.Has(user.Role, "Suppliers.Manage") ? Visibility.Visible : Visibility.Collapsed;
        SupplierPaymentPanel.Visibility = Permissions.Has(user.Role, "Payments.Make") ? Visibility.Visible : Visibility.Collapsed;
        ExpensesTab.Visibility = Permissions.Has(user.Role, "Expenses.Manage") ? Visibility.Visible : Visibility.Collapsed;
        CreditTab.Visibility = Permissions.Has(user.Role, "Payments.Receive") ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = Permissions.Has(user.Role, "Settings.View") || Permissions.Has(user.Role, "Database.Backup") ? Visibility.Visible : Visibility.Collapsed;
        AuditTab.Visibility = Permissions.Has(user.Role, "Audit.View") ? Visibility.Visible : Visibility.Collapsed;
        UsersTab.Visibility = Permissions.Has(user.Role, "Users.Manage") ? Visibility.Visible : Visibility.Collapsed;
        SaveSettingButton.Visibility = NewSettingButton.Visibility = Permissions.Has(user.Role, "Settings.Manage") ? Visibility.Visible : Visibility.Collapsed;
        BackupButton.Visibility = RestoreButton.Visibility = Permissions.Has(user.Role, "Database.Backup") ? Visibility.Visible : Visibility.Collapsed;
        if (!_backup.SupportsCurrentProvider)
            BackupButton.IsEnabled = RestoreButton.IsEnabled = false;
        MainTabs.SelectedItem = new[] { CustomersTab, SuppliersTab, ExpensesTab, SettingsTab, UsersTab, AuditTab, CreditTab }.FirstOrDefault(x => x.Visibility == Visibility.Visible);
        Loaded += async (_, _) => { await _viewModel.InitializeAsync(); if (SettingsTab.Visibility == Visibility.Visible) await _settingsViewModel.LoadAsync(); if (UsersTab.Visibility == Visibility.Visible) await _userViewModel.InitializeAsync(); };
    }
    private void ApplyRuntimeSetting(SettingSavedEventArgs setting)
    {
        if (setting.Key.Equals("Theme", StringComparison.OrdinalIgnoreCase))
            AppTheme.Apply(Application.Current, setting.Value);
        if (setting.Key.Equals("ApplicationName", StringComparison.OrdinalIgnoreCase))
        {
            Application.Current.Resources["AppName"] = setting.Value;
            if (Application.Current.MainWindow is DashboardWindow dashboard) dashboard.Title = $"{setting.Value} | Dashboard";
        }
    }
    private void UserPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => _userViewModel.Password = UserPasswordBox.Password;
    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite backup (*.db)|*.db|All files (*.*)|*.*", FileName = $"smartpos-{DateTime.Now:yyyyMMdd-HHmmss}.db", AddExtension = true, DefaultExt = ".db",
            InitialDirectory = !string.IsNullOrWhiteSpace(_settingsViewModel.BackupLocation) && Directory.Exists(_settingsViewModel.BackupLocation)
                ? _settingsViewModel.BackupLocation : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _backup.CreateBackupAsync(dialog.FileName, _user.Id);
            await _settingsViewModel.LoadAsync(); ThemedMessageBox.Show("Database backup created.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ThemedMessageBox.Show(UserErrorHandler.GetMessage(ex, "Creating the database backup"), "Backup failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SQLite backup (*.db;*.bak)|*.db;*.bak|All files (*.*)|*.*", CheckFileExists = true,
            InitialDirectory = !string.IsNullOrWhiteSpace(_settingsViewModel.BackupLocation) && Directory.Exists(_settingsViewModel.BackupLocation)
                ? _settingsViewModel.BackupLocation : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog(this) != true) return;
        if (ThemedMessageBox.Show($"Restore this database backup? A safety copy of the current database will be created first. Restart {SmartPosIdentity.Name} after the restore.", "Confirm restore", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _backup.RestoreAsync(dialog.FileName, _user.Id);
            ThemedMessageBox.Show($"Backup restored. Close and restart {SmartPosIdentity.Name} to refresh the active session.", "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ThemedMessageBox.Show(UserErrorHandler.GetMessage(ex, "Restoring the database"), "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
