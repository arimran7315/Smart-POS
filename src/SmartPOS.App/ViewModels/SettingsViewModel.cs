using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed record SettingSavedEventArgs(string Key, string Value);

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    private readonly int _userId;
    private readonly string _role;
    private readonly bool _supportsBackup;
    private AppSetting? _selectedSetting;
    private string _settingKey = "";
    private string _settingValue = "";
    private string _backupLocation = "";
    private string _lastBackupText = "Last backup: not recorded";
    private string _status = "";

    public ObservableCollection<AppSetting> Settings { get; } = [];
    public AppSetting? SelectedSetting
    {
        get => _selectedSetting;
        set
        {
            _selectedSetting = value;
            SettingKey = value?.Key ?? "";
            SettingValue = value?.Value ?? "";
            OnChanged();
        }
    }
    public string SettingKey { get => _settingKey; set { _settingKey = value; OnChanged(); } }
    public string SettingValue { get => _settingValue; set { _settingValue = value; OnChanged(); } }
    public string BackupLocation { get => _backupLocation; private set { _backupLocation = value; OnChanged(); } }
    public string LastBackupText { get => _lastBackupText; private set { _lastBackupText = value; OnChanged(); } }
    public string Status { get => _status; private set { _status = value; OnChanged(); } }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }

    public event EventHandler<SettingSavedEventArgs>? SettingSaved;
    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SettingsService settings, AppUser user, bool supportsBackup)
    {
        _settings = settings;
        _userId = user.Id;
        _role = user.Role;
        _supportsBackup = supportsBackup;
        RefreshCommand = new(LoadAsync, () => Permissions.Has(_role, "Settings.View"));
        NewCommand = new(() =>
        {
            SelectedSetting = null;
            Status = "Enter a supported setting key and its value.";
            return Task.CompletedTask;
        }, () => Permissions.Has(_role, "Settings.Manage"));
        SaveCommand = new(SaveAsync, () => Permissions.Has(_role, "Settings.Manage"));
    }

    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settings.GetAllAsync(_userId);
            Settings.Clear();
            foreach (var item in settings.OrderBy(x => x.Key))
                Settings.Add(new AppSetting { Key = item.Key, Value = item.Value });

            BackupLocation = settings.GetValueOrDefault("BackupLocation") ?? "";
            LastBackupText = !_supportsBackup
                ? "This build provides database backup and restore for SQLite databases."
                : settings.TryGetValue("LastBackupUtc", out var value)
                    && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var date)
                        ? $"Last backup: {date.ToLocalTime():g}"
                        : "Last backup: not recorded";
            Status = $"Loaded {Settings.Count} setting(s).";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading application settings"); }
    }

    private async Task SaveAsync()
    {
        var key = SettingKey.Trim();
        var value = SettingValue.Trim();
        if (key.Length == 0) { Status = "Enter a setting key."; return; }
        try
        {
            await _settings.SetAsync(key, value, _userId);
            await LoadAsync();
            SelectedSetting = Settings.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            SettingSaved?.Invoke(this, new SettingSavedEventArgs(key, value));
            Status = $"Saved setting {key}.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving application settings"); }
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
