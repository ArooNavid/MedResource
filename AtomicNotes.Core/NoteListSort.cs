namespace AtomicNotes.Core;

/// <summary>Stage 37: server-side ordering for the notes list (pinned notes always first).</summary>
public enum NoteListSort
{
    Updated,
    Title,
    Created,
    Depth
}

public static class NoteListSortParser
{
    public static NoteListSort ParseSort(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "title" => NoteListSort.Title,
            "created" => NoteListSort.Created,
            "depth" => NoteListSort.Depth,
            _ => NoteListSort.Updated
        };

    public static bool ParseAscending(string? order) =>
        string.Equals(order?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
}
