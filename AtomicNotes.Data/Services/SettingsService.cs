using System.Text.Json;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Data.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public SettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtomicNotes",
            "settings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            if (!File.Exists(SettingsFilePath))
            {
                Current = CreateDefaults();
                WriteFile(Current);
                return Clone(Current);
            }

            var json = File.ReadAllText(SettingsFilePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaults();
            EnsurePaths(Current);
            return Clone(Current);
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            if (settings.LastAutoBackupAt is null)
                settings.LastAutoBackupAt = Current.LastAutoBackupAt;
            EnsurePaths(settings);
            Current = Clone(settings);
            WriteFile(Current);
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            WriteFile(Current);
        }
    }

    private void WriteFile(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        var temp = SettingsFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        if (File.Exists(SettingsFilePath))
            File.Delete(SettingsFilePath);
        File.Move(temp, SettingsFilePath);
    }

    private AppSettings CreateDefaults()
    {
        var root = Path.GetDirectoryName(SettingsFilePath)!;
        var vault = Path.Combine(root, "vault");
        return new AppSettings
        {
            VaultPath = vault,
            BackupPath = Path.Combine(root, "backups"),
            DatabasePath = Path.Combine(vault, AppConstants.DbFileName),
            BackupIntervalHours = 24,
            Theme = "System",
            NotificationsEnabled = true
        };
    }

    private static void EnsurePaths(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DatabasePath) && !string.IsNullOrWhiteSpace(settings.VaultPath))
            settings.DatabasePath = Path.Combine(settings.VaultPath, AppConstants.DbFileName);
    }

    private static AppSettings Clone(AppSettings source) => new()
    {
        VaultPath = source.VaultPath,
        BackupPath = source.BackupPath,
        DatabasePath = source.DatabasePath,
        BackupIntervalHours = source.BackupIntervalHours,
        Theme = source.Theme,
        NotificationsEnabled = source.NotificationsEnabled,
        LastAutoBackupAt = source.LastAutoBackupAt
    };
}
