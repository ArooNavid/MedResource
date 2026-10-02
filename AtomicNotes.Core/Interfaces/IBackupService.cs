using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IBackupService
{
    Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default);

    Task DeleteBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default);
}
