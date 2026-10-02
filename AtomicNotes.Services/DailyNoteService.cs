using System.Globalization;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 22: one note per Tehran day, stored at daily/yyyy-MM-dd.md.
/// Opening a day that already has a file keeps that file.
/// </summary>
public sealed class DailyNoteService : IDailyNoteService
{
    private readonly ISettingsService _settings;
    private readonly ITehranClockService _clock;
    private readonly NoteService _notes;
    private readonly ITemplateService _templates;

    public DailyNoteService(ISettingsService settings, ITehranClockService clock, NoteService notes, ITemplateService templates)
    {
        _settings = settings;
        _clock = clock;
        _notes = notes;
        _templates = templates;
    }

    public async Task<DailyNoteResult> OpenAsync(long ownerUserId, string? tehranDate = null, CancellationToken ct = default)
    {
        var date = string.IsNullOrWhiteSpace(tehranDate) ? _clock.TehranDateString : tehranDate.Trim();
        if (!DateTime.TryParseExact(date, AppConstants.TehranDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new InvalidOperationException("تاریخ یادداشت روزانه باید به شکل yyyy-MM-dd باشد.");

        var rel = $"{AppConstants.DailyNotesFolder}/{date}{AppConstants.MarkdownExtension}";
        var existing = await _notes.FindByRelPathAsync(rel, ct);
        if (existing is not null)
            return new DailyNoteResult(existing, false, date, rel);

        var full = Path.Combine(_settings.Current.VaultPath, AppConstants.DailyNotesFolder, date + AppConstants.MarkdownExtension);
        if (File.Exists(full))
        {
            await _notes.UpsertFromFileAsync(full, ct);
            var adopted = await _notes.FindByRelPathAsync(rel, ct)
                ?? throw new InvalidOperationException("یادداشت روزانه از فایل خوانده نشد.");
            return new DailyNoteResult(adopted, false, date, rel);
        }

        var previous = day.AddDays(-1).ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var next = day.AddDays(1).ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
        var fromTemplate = await _templates.RenderDailyAsync(day, ct);
        var content = fromTemplate?.Content ?? $"""
            [[{previous}]] · [[{next}]]

            ## کارها

            - 

            ## یادداشت

            """;
        var tags = fromTemplate?.Tags ?? new[] { AppConstants.DailyNoteTag };
        var created = await _notes.CreateAtPathAsync(ownerUserId, date, content, rel, tags, ct);
        return new DailyNoteResult(created, true, date, rel);
    }

    public async Task<DailyJournalMonth> GetMonthAsync(int year, int month, CancellationToken ct = default)
    {
        if (month is < 1 or > 12)
            throw new InvalidOperationException("ماه باید بین ۱ تا ۱۲ باشد.");
        if (year is < 2000 or > 2100)
            throw new InvalidOperationException("سال معتبر نیست.");

        var first = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var prefix = $"{AppConstants.DailyNotesFolder}/{year:D4}-{month:D2}-";
        var notes = (await _notes.ListAsync(ct))
            .Where(note => note.RelPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                note => Path.GetFileNameWithoutExtension(note.RelPath.Replace('\\', '/')),
                note => note,
                StringComparer.Ordinal);

        var folder = Path.Combine(_settings.Current.VaultPath, AppConstants.DailyNotesFolder);
        var days = new List<DailyJournalDay>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(year, month, day).ToString(AppConstants.TehranDateFormat, CultureInfo.InvariantCulture);
            notes.TryGetValue(date, out var note);
            var onDisk = Directory.Exists(folder) && File.Exists(Path.Combine(folder, date + AppConstants.MarkdownExtension));
            days.Add(new DailyJournalDay(date, note is not null || onDisk, note?.Id));
        }

        var leading = ((int)first.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        return new DailyJournalMonth(year, month, _clock.TehranDateString, leading, days);
    }
}
