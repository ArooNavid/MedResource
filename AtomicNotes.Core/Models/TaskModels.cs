namespace AtomicNotes.Core.Models;

public sealed record NoteTaskItem(long NoteId, string NoteTitle, int LineIndex, string Text, bool IsDone);

public sealed record TaskListSummary(int OpenCount, int DoneCount, IReadOnlyList<NoteTaskItem> Items);
