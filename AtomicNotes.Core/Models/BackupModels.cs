namespace AtomicNotes.Core.Models;

public sealed record BackupInfo(
    string FilePath,
    string FileName,
    DateTime CreatedAt,
    long SizeBytes)
{
    public string DisplaySize => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024):F1} MB"
    };
}

public sealed record BackupResult(bool Success, string? FilePath = null, string? Error = null);

public sealed record RestoreResult(bool Success, string? Error = null);
