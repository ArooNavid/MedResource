using System.Globalization;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 23: vault templates under templates/*.md.
/// Placeholders use the Tehran clock. templates/daily.md fills a new daily note.
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ISettingsService _settings;
    private readonly ITehranClockService _clock;
    private readonly NoteService _notes;
    private readonly ITagService _tags;
    private readonly VaultWriteGuard _guard;

    public TemplateService(
        ISettingsService settings,
        ITehranClockService clock,
        NoteService notes,
        ITagService tags,
        VaultWriteGuard guard)
    {
        _settings = settings;
        _clock = clock;
        _notes = notes;
        _tags = tags;
        _guard = guard;
    }

    public Task<IReadOnlyList<NoteTemplate>> ListAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var folder = Folder();
        if (!Directory.Exists(folder))
            return Task.FromResult<IReadOnlyList<NoteTemplate>>(Array.Empty<NoteTemplate>());

        var templates = Directory.EnumerateFiles(folder, "*" + AppConstants.MarkdownExtension, SearchOption.TopDirectoryOnly)
            .Select(Read)
            .OrderBy(template => template.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<NoteTemplate>>(templates);
    }

    public Task<NoteTemplate> CreateAsync(string title, string content, IEnumerable<string> tags, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        title = (title ?? "").Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("نام قالب نمی‌تواند خالی باشد.");

        var name = Path.GetFileNameWithoutExtension(MarkdownFiles.SanitizeFileName(title));
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\'))
            throw new InvalidOperationException("نام قالب نمی‌تواند خالی باشد.");

        var folder = Folder();
        Directory.CreateDirectory(folder);
        if (FindPath(name) is not null)
            throw new InvalidOperationException("این قالب وجود دارد.");

        var tagList = CleanTags(tags);
        var full = Path.Combine(folder, name + AppConstants.MarkdownExtension);
        _guard.Suppress(full);
        File.WriteAllText(full, MarkdownFiles.Compose(title, 1, tagList, content ?? ""));
        return Task.FromResult(Read(full));
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = FindPath(RequireName(name)) ?? throw new InvalidOperationException("قالب پیدا نشد.");
        _guard.Suppress(path);
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<RenderedTemplate> RenderAsync(string name, string? title, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = FindPath(RequireName(name)) ?? throw new InvalidOperationException("قالب پیدا نشد.");
        var template = Read(path);
        var when = ParseDay(_clock.TehranDateString);
        var rendered = Render(template, string.IsNullOrWhiteSpace(title) ? template.Title : title.Trim(), when);
        return Task.FromResult(rendered);
    }

    public Task<RenderedTemplate?> RenderDailyAsync(DateTime day, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = FindPath(AppConstants.DailyTemplateName);
        if (path is null)
            return Task.FromResult<RenderedTemplate?>(null);

        var template = Read(path);
        var date = day.ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var rendered = Render(template, date, day);
        var tags = rendered.Tags.Count > 0 ? rendered.Tags : new[] { AppConstants.DailyNoteTag };
        return Task.FromResult<RenderedTemplate?>(rendered with { Tags = tags });
    }

    public async Task<Note> ApplyAsync(long noteId, long editorUserId, string name, CancellationToken ct = default)
    {
        var note = await _notes.GetAsync(noteId, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        var rendered = await RenderAsync(name, note.Title, ct);
        var addition = rendered.Content.Replace("\r\n", "\n").TrimEnd();
        var content = string.IsNullOrWhiteSpace(note.Content)
            ? addition
            : note.Content.Replace("\r\n", "\n").TrimEnd() + "\n\n" + addition;
        var existing = (await _tags.GetTagsForNoteAsync((int)noteId, ct)).Select(tag => tag.Name);
        var tags = existing
            .Concat(rendered.Tags)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return await _notes.UpdateAsync(noteId, editorUserId, note.Title, content, tags, ct);
    }

    private RenderedTemplate Render(NoteTemplate template, string title, DateTime day)
    {
        var date = day.ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var previous = day.AddDays(-1).ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var next = day.AddDays(1).ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var time = _clock.TehranNow.ToString("HH:mm", CultureInfo.InvariantCulture);
        var content = template.Content
            .Replace("{{title}}", title)
            .Replace("{{date}}", date)
            .Replace("{{yesterday}}", previous)
            .Replace("{{tomorrow}}", next)
            .Replace("{{time}}", time);
        return new RenderedTemplate(content, template.Tags);
    }

    private NoteTemplate Read(string fullPath)
    {
        var parsed = MarkdownFiles.Parse(File.ReadAllText(fullPath));
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? name : parsed.Title;
        return new NoteTemplate(name, title, parsed.Tags, parsed.Body);
    }

    private string? FindPath(string name)
    {
        var folder = Folder();
        if (!Directory.Exists(folder))
            return null;
        return Directory.EnumerateFiles(folder, "*" + AppConstants.MarkdownExtension, SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase));
    }

    private string Folder()
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            throw new InvalidOperationException("مسیر Vault تنظیم نشده است.");
        return Path.Combine(vault, AppConstants.TemplatesFolder);
    }

    private static string RequireName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("نام قالب نمی‌تواند خالی باشد.");
        return name;
    }

    private static DateTime ParseDay(string date) =>
        DateTime.ParseExact(date, AppConstants.TehranDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string[] CleanTags(IEnumerable<string> tags) =>
        (tags ?? Array.Empty<string>())
        .Select(tag => tag.Trim())
        .Where(tag => tag.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
