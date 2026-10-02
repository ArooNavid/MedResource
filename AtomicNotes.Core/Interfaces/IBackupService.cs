using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync();
    Task<RestoreResult> RestoreBackupAsync(BackupInfo backup, CancellationToken ct = default);
    Task DeleteBackupAsync(BackupInfo backup);
    Task<AutoBackupResult> RunAutoBackupAsync(CancellationToken ct = default);
}
