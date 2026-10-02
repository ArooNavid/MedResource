namespace AtomicNotes.Core.Models;

public enum ImportStatus
{
    Prepared,
    Staged,
    Committed,
    Failed
}

public sealed class ImportOperation
{
    public long Id { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string PdfHash { get; set; } = string.Empty;
    public string OriginalPdfPath { get; set; } = string.Empty;
    public string? StagingPath { get; set; }
    public string? FinalRelativePath { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Prepared;
    public string? ErrorMessage { get; set; }
}

public sealed record ImportOutcome(
    bool Success,
    string Status,
    string? OperationId,
    int NotesCreated,
    string? Error,
    bool Duplicate);
