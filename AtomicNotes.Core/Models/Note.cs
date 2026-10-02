namespace AtomicNotes.Core.Models;

public sealed class Note
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string RelPath { get; set; } = string.Empty;
    public int Depth { get; set; } = 1;
    public long? ParentNoteId { get; set; }
    public long? OwnerUserId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Pinned { get; set; }
    /// <summary>UTC ISO-8601 when moved to trash; null means active.</summary>
    public string? DeletedAt { get; set; }
}
