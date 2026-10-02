using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.WPF.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private string _vaultPath = string.Empty;
    private string _backupPath = string.Empty;
    private string _databasePath = string.Empty;
    private bool _autoBackupEnabled;
    private int _autoBackupIntervalHours = 24;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string SettingsFilePath => _settingsService.SettingsFilePath;

    public string VaultPath
    {
        get => _vaultPath;
        set => SetProperty(ref _vaultPath, value);
    }

    public string BackupPath
    {
        get => _backupPath;
        set => SetProperty(ref _backupPath, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        set => SetProperty(ref _databasePath, value);
    }

    public bool AutoBackupEnabled
    {
        get => _autoBackupEnabled;
        set => SetProperty(ref _autoBackupEnabled, value);
    }

    public int AutoBackupIntervalHours
    {
        get => _autoBackupIntervalHours;
        set => SetProperty(ref _autoBackupIntervalHours, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        Apply(settings);
        StatusMessage = string.Empty;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _settingsService.SaveAsync(Current(), cancellationToken).ConfigureAwait(false);
        StatusMessage = "تنظیمات ذخیره شد.";
    }

    private void Apply(AppSettings settings)
    {
        VaultPath = settings.VaultPath;
        BackupPath = settings.BackupPath;
        DatabasePath = settings.DatabasePath;
        AutoBackupEnabled = settings.AutoBackupEnabled;
        AutoBackupIntervalHours = settings.AutoBackupIntervalHours;
    }

    private AppSettings Current() => new()
    {
        VaultPath = VaultPath,
        BackupPath = BackupPath,
        DatabasePath = DatabasePath,
        AutoBackupEnabled = AutoBackupEnabled,
        AutoBackupIntervalHours = AutoBackupIntervalHours
    };
}
