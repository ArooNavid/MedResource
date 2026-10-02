using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 25: markdown checkboxes (- [ ] / - [x]) across vault notes.
/// </summary>
public sealed class TaskService : ITaskService
{
    private readonly INoteService _notes;
    private readonly ITagService _tags;

    public TaskService(INoteService notes, ITagService tags)
    {
        _notes = notes;
        _tags = tags;
    }

    public async Task<TaskListSummary> ListAsync(bool? openOnly = null, CancellationToken ct = default)
    {
        var allItems = new List<NoteTaskItem>();
        foreach (var note in await _notes.ListAsync(ct))
        {
            foreach (var task in MarkdownTasks.Parse(note.Content))
                allItems.Add(new NoteTaskItem(note.Id, note.Title, task.LineIndex, task.Text, task.IsDone));
        }

        var openCount = allItems.Count(item => !item.IsDone);
        var doneCount = allItems.Count(item => item.IsDone);
        var filtered = openOnly switch
        {
            true => allItems.Where(item => !item.IsDone),
            false => allItems.Where(item => item.IsDone),
            _ => allItems.AsEnumerable()
        };

        return new TaskListSummary(openCount, doneCount, filtered
            .OrderBy(item => item.IsDone)
            .ThenBy(item => item.NoteTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LineIndex)
            .ToList());
    }

    public async Task<Note> ToggleAsync(long noteId, long editorUserId, int lineIndex, bool done, CancellationToken ct = default)
    {
        var note = await _notes.GetAsync(noteId, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        var content = MarkdownTasks.ToggleLine(note.Content, lineIndex, done);
        var tags = (await _tags.GetTagsForNoteAsync((int)noteId, ct)).Select(tag => tag.Name);
        return await _notes.UpdateAsync(noteId, editorUserId, note.Title, content, tags, ct);
    }
}
