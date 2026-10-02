using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Data.Repositories;

public sealed class ImportOperationRepository : IImportOperationRepository
{
    private readonly IDbConnectionFactory _factory;

    public ImportOperationRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<long> InsertAsync(ImportOperation operation, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                INSERT INTO import_operations
                    (operation_id, pdf_hash, original_pdf_path, staging_path, final_relative_path,
                     status, started_utc, updated_utc, error_message)
                VALUES
                    (@OperationId, @PdfHash, @OriginalPdfPath, @StagingPath, @FinalRelativePath,
                     @Status, @StartedUtc, @UpdatedUtc, @ErrorMessage);
                SELECT last_insert_rowid();
                """,
                new
                {
                    operation.OperationId,
                    operation.PdfHash,
                    operation.OriginalPdfPath,
                    operation.StagingPath,
                    operation.FinalRelativePath,
                    Status = operation.Status.ToString(),
                    StartedUtc = operation.StartedUtc.ToUniversalTime().ToString("o"),
                    UpdatedUtc = operation.UpdatedUtc.ToUniversalTime().ToString("o"),
                    operation.ErrorMessage
                },
                cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(
        long id,
        ImportStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var updated = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE import_operations
                   SET status = @Status,
                       updated_utc = @UpdatedUtc,
                       error_message = COALESCE(@ErrorMessage, error_message)
                 WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    Status = status.ToString(),
                    UpdatedUtc = DateTime.UtcNow.ToString("o"),
                    ErrorMessage = errorMessage
                },
                cancellationToken: cancellationToken));

        if (updated == 0)
            throw new InvalidOperationException($"Import operation {id} was not found.");
    }

    public async Task<ImportStatus?> GetStatusAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var status = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT status FROM import_operations WHERE id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));

        return status is null ? null : Enum.Parse<ImportStatus>(status);
    }
}
