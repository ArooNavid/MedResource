namespace AtomicNotes.Core.Models;

/// <summary>
/// Pipeline states stored in import_operations.status.
/// Committed is the official terminal success value.
/// </summary>
public enum ImportStatus
{
    Prepared,
    Staged,
    Committed,
    Failed
}
