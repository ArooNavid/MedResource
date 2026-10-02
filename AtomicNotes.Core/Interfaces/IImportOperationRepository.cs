using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IImportOperationRepository
{
    Task<long> InsertAsync(ImportOperation operation, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        long id,
        ImportStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    Task<ImportStatus?> GetStatusAsync(long id, CancellationToken cancellationToken = default);
}
