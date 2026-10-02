using System.Security.Cryptography;
using System.Text;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 21: two-way sync between SQLite notes and Obsidian markdown files.
/// Frontmatter carries title, depth, tags, created, and updated.
/// </summary>
public sealed class ObsidianSyncService : IObsidianSyncService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly NoteService _notes;
    private readonly ITagService _tags;
    private readonly VaultWriteGuard _guard;
    private readonly ITehranClockService _clock;

    public ObsidianSyncService(
        IDbConnectionFactory factory,
        ISettingsService settings,
        NoteService notes,
        ITagService tags,
        VaultWriteGuard guard,
        ITehranClockService clock)
    {
        _factory = factory;
        _settings = settings;
        _notes = notes;
        _tags = tags;
        _guard = guard;
        _clock = clock;
    }

    public SyncReport? LastReport { get; private set; }

    public async Task<SyncReport> SyncAllAsync(CancellationToken ct = default)
    {
        var vault = EnsureVault();
        var notes = (await _notes.ListAsync(ct)).ToList();
        var tagsByNote = await LoadTagsAsync(ct);
        var syncRows = await LoadSyncAsync(ct);
        var files = EnumerateMarkdown(vault);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pulled = 0;
        var pushed = 0;
        var deleted = 0;
        var unchanged = 0;
        var conflicts = 0;
        var messages = new List<string>();

        foreach (var note in notes)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(note.RelPath))
                continue;
            seen.Add(note.RelPath);
            files.TryGetValue(note.RelPath, out var fullPath);
            var tags = tagsByNote.TryGetValue(note.Id, out var names) ? names : Array.Empty<string>();
            syncRows.TryGetValue(note.Id, out var state);
            var outcome = await ReconcileAsync(note, tags, state, fullPath, messages, ct);
            switch (outcome)
            {
                case SyncAction.Pull: pulled++; break;
                case SyncAction.Push: pushed++; break;
                case SyncAction.Delete: deleted++; break;
                case SyncAction.ConflictPull: conflicts++; pulled++; break;
                case SyncAction.ConflictPush: conflicts++; pushed++; break;
                default: unchanged++; break;
            }
        }

        foreach (var (rel, full) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (seen.Contains(rel))
                continue;
            await _notes.UpsertFromFileAsync(full, ct);
            var created = (await _notes.ListAsync(ct)).First(item => string.Equals(item.RelPath, rel, StringComparison.OrdinalIgnoreCase));
            var createdTags = (await _tags.GetTagsForNoteAsync((int)created.Id, ct)).Select(tag => tag.Name).ToArray();
            await SaveSyncAsync(created.Id, HashFile(full), ProjectionHash(created, createdTags), ct);
            pulled++;
            messages.Add($"از فایل خوانده شد: {created.Title}");
        }

        LastReport = new SyncReport(DateTime.UtcNow, pulled, pushed, deleted, unchanged, conflicts, messages);
        return LastReport;
    }

    public async Task PullFileAsync(string fullPath, CancellationToken ct = default)
    {
        if (!ShouldTouch(fullPath) || !File.Exists(fullPath))
            return;
        await _notes.UpsertFromFileAsync(fullPath, ct);
        var rel = Relative(fullPath);
        var note = (await _notes.ListAsync(ct)).FirstOrDefault(item => string.Equals(item.RelPath, rel, StringComparison.OrdinalIgnoreCase));
        if (note is null)
            return;
        var tags = (await _tags.GetTagsForNoteAsync((int)note.Id, ct)).Select(tag => tag.Name).ToArray();
        await SaveSyncAsync(note.Id, HashFile(fullPath), ProjectionHash(note, tags), ct);
    }

    public async Task OnFileMissingAsync(string fullPath, CancellationToken ct = default)
    {
        if (!ShouldTouch(fullPath))
            return;
        var rel = Relative(fullPath);
        var note = (await _notes.ListAsync(ct)).FirstOrDefault(item => string.Equals(item.RelPath, rel, StringComparison.OrdinalIgnoreCase));
        if (note is null)
            return;
        var tags = (await _tags.GetTagsForNoteAsync((int)note.Id, ct)).Select(tag => tag.Name).ToArray();
        var syncRows = await LoadSyncAsync(ct);
        syncRows.TryGetValue(note.Id, out var state);
        await ReconcileAsync(note, tags, state, fullPath: null, new List<string>(), ct);
    }

    private async Task<SyncAction> ReconcileAsync(
        Note note,
        IReadOnlyList<string> tags,
        SyncState? state,
        string? fullPath,
        List<string> messages,
        CancellationToken ct)
    {
        var dbHash = ProjectionHash(note, tags);
        var fileExists = fullPath is not null && File.Exists(fullPath);
        if (!fileExists)
        {
            if (state is not null && state.DbHash == dbHash)
            {
                await _notes.DeleteAsync(note.Id, ct);
                messages.Add($"حذف شد چون فایل خزانه نبود: {note.Title}");
                return SyncAction.Delete;
            }

            WriteNoteFile(note, tags);
            await SaveSyncAsync(note.Id, HashFile(FullPath(note.RelPath)), dbHash, ct);
            messages.Add($"در خزانه نوشته شد: {note.Title}");
            return SyncAction.Push;
        }

        var fileHash = HashFile(fullPath!);
        var parsed = MarkdownFiles.Parse(await File.ReadAllTextAsync(fullPath!, ct));
        var fileChanged = state is null ? !SameContent(note, tags, parsed) : state.FileHash != fileHash;
        var dbChanged = state is not null && state.DbHash != dbHash;

        if (state is null && !fileChanged)
        {
            await SaveSyncAsync(note.Id, fileHash, dbHash, ct);
            return SyncAction.None;
        }

        if (fileChanged && dbChanged)
        {
            var fileTime = File.GetLastWriteTimeUtc(fullPath!);
            var noteTime = ParseTime(note.UpdatedAt);
            if (fileTime >= noteTime)
            {
                await _notes.UpsertFromFileAsync(fullPath!, ct);
                var refreshed = (await _notes.GetAsync(note.Id, ct))!;
                var refreshedTags = (await _tags.GetTagsForNoteAsync((int)note.Id, ct)).Select(tag => tag.Name).ToArray();
                await SaveSyncAsync(note.Id, HashFile(fullPath!), ProjectionHash(refreshed, refreshedTags), ct);
                messages.Add($"تعارض در «{note.Title}»: نسخهٔ جدیدتر فایل اعمال شد.");
                return SyncAction.ConflictPull;
            }

            WriteNoteFile(note, tags);
            await SaveSyncAsync(note.Id, HashFile(fullPath!), dbHash, ct);
            messages.Add($"تعارض در «{note.Title}»: نسخهٔ جدیدتر پایگاه‌داده در فایل نوشته شد.");
            return SyncAction.ConflictPush;
        }

        if (fileChanged)
        {
            await _notes.UpsertFromFileAsync(fullPath!, ct);
            var refreshed = (await _notes.GetAsync(note.Id, ct))!;
            var refreshedTags = (await _tags.GetTagsForNoteAsync((int)note.Id, ct)).Select(tag => tag.Name).ToArray();
            await SaveSyncAsync(note.Id, HashFile(fullPath!), ProjectionHash(refreshed, refreshedTags), ct);
            messages.Add($"از فایل به‌روز شد: {refreshed.Title}");
            return SyncAction.Pull;
        }

        if (dbChanged)
        {
            WriteNoteFile(note, tags);
            await SaveSyncAsync(note.Id, HashFile(fullPath!), dbHash, ct);
            messages.Add($"فایل خزانه به‌روز شد: {note.Title}");
            return SyncAction.Push;
        }

        return SyncAction.None;
    }

    private void WriteNoteFile(Note note, IReadOnlyList<string> tags)
    {
        var full = FullPath(note.RelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var created = TehranDay(note.CreatedAt);
        var updated = TehranDay(note.UpdatedAt);
        var markdown = MarkdownFiles.Compose(note.Title, note.Depth, tags, note.Content, created: created, updated: updated);
        _guard.Suppress(full);
        File.WriteAllText(full, markdown);
    }

    private string EnsureVault()
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            throw new InvalidOperationException("مسیر Vault تنظیم نشده است.");
        Directory.CreateDirectory(vault);
        return vault;
    }

    private Dictionary<string, string> EnumerateMarkdown(string vault)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories))
        {
            if (!ShouldTouch(file))
                continue;
            map[Relative(file)] = file;
        }
        return map;
    }

    private bool ShouldTouch(string fullPath)
    {
        var rel = Relative(fullPath);
        return !rel.StartsWith(".staging/", StringComparison.OrdinalIgnoreCase)
               && !rel.Contains("/.staging/", StringComparison.OrdinalIgnoreCase);
    }

    private string Relative(string fullPath)
    {
        var vault = _settings.Current.VaultPath;
        return Path.GetRelativePath(vault, fullPath).Replace('\\', '/');
    }

    private string FullPath(string rel) =>
        Path.Combine(_settings.Current.VaultPath, rel.Replace('/', Path.DirectorySeparatorChar));

    private async Task<Dictionary<long, string[]>> LoadTagsAsync(CancellationToken ct)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<(long NoteId, string Name)>(
            new CommandDefinition(
                """
                SELECT nt.note_id AS NoteId, t.name AS Name
                  FROM note_tags nt
                  JOIN tags t ON t.id = nt.tag_id
                """,
                cancellationToken: ct));
        return rows
            .GroupBy(row => row.NoteId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Name).ToArray());
    }

    private async Task<Dictionary<long, SyncState>> LoadSyncAsync(CancellationToken ct)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<SyncState>(
            new CommandDefinition(
                "SELECT note_id AS NoteId, file_hash AS FileHash, db_hash AS DbHash FROM vault_sync",
                cancellationToken: ct));
        return rows.ToDictionary(row => row.NoteId);
    }

    private async Task SaveSyncAsync(long noteId, string fileHash, string dbHash, CancellationToken ct)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO vault_sync (note_id, file_hash, db_hash, synced_at)
                VALUES (@Id, @FileHash, @DbHash, @At)
                ON CONFLICT(note_id) DO UPDATE SET
                    file_hash = excluded.file_hash,
                    db_hash = excluded.db_hash,
                    synced_at = excluded.synced_at
                """,
                new { Id = noteId, FileHash = fileHash, DbHash = dbHash, At = DateTime.UtcNow.ToString("o") },
                cancellationToken: ct));
    }

    private static string ProjectionHash(Note note, IEnumerable<string> tags)
    {
        var tagList = tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
        var projection = string.Join('\u001f', note.Title, note.Depth, string.Join(',', tagList), note.Content.Replace("\r\n", "\n").TrimEnd());
        return HashText(projection);
    }

    private static bool SameContent(Note note, IReadOnlyList<string> tags, MarkdownFiles.MarkdownDocument parsed)
    {
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? note.Title : parsed.Title;
        var fileTags = parsed.Tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
        var dbTags = tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);
        return string.Equals(note.Title, title, StringComparison.Ordinal)
               && string.Equals(note.Content.Replace("\r\n", "\n").TrimEnd(), parsed.Body.Replace("\r\n", "\n").TrimEnd(), StringComparison.Ordinal)
               && fileTags.SequenceEqual(dbTags, StringComparer.OrdinalIgnoreCase);
    }

    private static string HashFile(string path) => HashText(File.ReadAllText(path).Replace("\r\n", "\n"));

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private string TehranDay(string timestamp)
    {
        if (DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            return _clock.FormatTehranDate(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return _clock.TehranDateString;
    }

    private static DateTime ParseTime(string timestamp) =>
        DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;

    private enum SyncAction
    {
        None,
        Pull,
        Push,
        Delete,
        ConflictPull,
        ConflictPush
    }

    private sealed class SyncState
    {
        public long NoteId { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string DbHash { get; set; } = string.Empty;
    }
}
