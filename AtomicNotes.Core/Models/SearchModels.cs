namespace AtomicNotes.Core.Models;

public sealed record SearchResult(int NoteId, string Title, string Snippet, double Rank);

public sealed record Tag
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#6C757D";
    public string CreatedAt { get; init; } = string.Empty;
    public int NoteCount { get; init; }
}

public sealed record NoteLink
{
    public int Id { get; init; }
    public int SourceNoteId { get; init; }
    public int? TargetNoteId { get; init; }
    public string RawTarget { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public string SourceTitle { get; init; } = string.Empty;
    public string TargetTitle { get; init; } = string.Empty;
    public bool IsResolved => TargetNoteId.HasValue;
}
