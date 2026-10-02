namespace AtomicNotes.Core.Models;

public sealed class AppSettings
{
    public string VaultPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = string.Empty;
    public int BackupIntervalHours { get; set; } = 24;
    public string Theme { get; set; } = "System";
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>UTC timestamp written by the auto-backup scheduler. Not edited in the settings form.</summary>
    public DateTime? LastAutoBackupAt { get; set; }
}
