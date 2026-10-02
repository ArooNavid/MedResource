using System.Globalization;
using System.Text;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 27: download vault-compatible markdown and import a single .md file.
/// </summary>
public sealed class NoteMarkdownService : INoteMarkdownService
{
    private readonly INoteService _notes;
    private readonly ITagService _tags;
    private readonly ITehranClockService _clock;
    private readonly NoteService _noteWriter;

    public NoteMarkdownService(
        INoteService notes,
        ITagService tags,
        ITehranClockService clock,
        NoteService noteWriter)
    {
        _notes = notes;
        _tags = tags;
        _clock = clock;
        _noteWriter = noteWriter;
    }

    public async Task<MarkdownExportResult> ExportAsync(long noteId, CancellationToken ct = default)
    {
        var note = await _notes.GetAsync(noteId, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        var tagNames = (await _tags.GetTagsForNoteAsync((int)noteId, ct)).Select(tag => tag.Name);
        var markdown = MarkdownFiles.Compose(
            note.Title,
            note.Depth,
            tagNames,
            note.Content,
            created: TehranDay(note.CreatedAt),
            updated: TehranDay(note.UpdatedAt));
        var fileName = string.IsNullOrWhiteSpace(note.RelPath)
            ? MarkdownFiles.SanitizeFileName(note.Title) + AppConstants.MarkdownExtension
            : Path.GetFileName(note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        return new MarkdownExportResult(fileName, markdown);
    }

    public async Task<Note> ImportAsync(long ownerUserId, string fileName, Stream markdown, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(AppConstants.MarkdownExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط فایل .md پذیرفته می‌شود.");

        using var reader = new StreamReader(markdown, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("فایل مارک‌داون خالی است.");

        var parsed = MarkdownFiles.Parse(text);
        var title = string.IsNullOrWhiteSpace(parsed.Title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : parsed.Title.Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان یادداشت در فایل پیدا نشد.");

        var rel = await _noteWriter.AllocateRelPathAsync(title, null, ct);
        return await _noteWriter.CreateAtPathAsync(ownerUserId, title, parsed.Body, rel, parsed.Tags, ct);
    }

    private string TehranDay(string timestamp)
    {
        if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return _clock.FormatTehranDate(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return _clock.TehranDateString;
    }
}
