namespace AtomicNotes.Core.Models;

public sealed record NoteTemplate(string Name, string Title, IReadOnlyList<string> Tags, string Content);

public sealed record RenderedTemplate(string Content, IReadOnlyList<string> Tags);
