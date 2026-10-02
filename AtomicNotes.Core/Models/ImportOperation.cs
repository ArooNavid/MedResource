namespace AtomicNotes.Core.Models;

public sealed class ImportOperation
{
    public long Id { get; set; }
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public string PdfHash { get; set; } = string.Empty;
    public string OriginalPdfPath { get; set; } = string.Empty;
    public string? StagingPath { get; set; }
    public string? FinalRelativePath { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Prepared;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
