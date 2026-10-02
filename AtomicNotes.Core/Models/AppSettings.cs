namespace AtomicNotes.Core.Models;

public sealed class AppSettings
{
    public string VaultPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public bool AutoBackupEnabled { get; set; }
    public int AutoBackupIntervalHours { get; set; } = 24;
    public string Theme { get; set; } = "Light";
    public bool ShowNotifications { get; set; } = true;
    public string DatabasePath { get; set; } = string.Empty;
}
