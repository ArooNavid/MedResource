using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

/// <summary>Stage 48: find markdown files not indexed in DB and active notes missing files.</summary>
public sealed class VaultDriftService : IVaultDriftService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly NoteService _notes;

    public VaultDriftService(IDbConnectionFactory factory, ISettingsService settings, NoteService notes)
    {
        _factory = factory;
        _settings = settings;
        _notes = notes;
    }

    public async Task<VaultDriftReport> GetReportAsync(CancellationToken ct = default)
    {
        var vault = RequireVaultPath();
        var knownPaths = await LoadKnownRelPathsAsync(ct);
        var knownSet = new HashSet<string>(knownPaths.Keys, StringComparer.OrdinalIgnoreCase);

        var onlyOnDisk = new List<VaultDriftEntry>();
        if (Directory.Exists(vault))
        {
            foreach (var fullPath in Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(vault, fullPath).Replace('\\', '/');
                if (AppConstants.IsIgnoredVaultRelativePath(rel))
                    continue;
                if (knownSet.Contains(rel))
                    continue;
                onlyOnDisk.Add(new VaultDriftEntry(rel, null, null));
            }
        }

        onlyOnDisk.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));

        var missingFile = new List<VaultDriftEntry>();
        using var connection = _factory.Create();
        var activeNotes = await connection.QueryAsync<NoteRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, title AS Title, rel_path AS RelPath
                  FROM notes
                 WHERE deleted_at IS NULL
                   AND rel_path IS NOT NULL
                   AND TRIM(rel_path) <> ''
                ORDER BY rel_path COLLATE NOCASE
                """,
                cancellationToken: ct));

        foreach (var note in activeNotes)
        {
            var rel = note.RelPath!.Replace('\\', '/');
            var full = Path.Combine(vault, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                missingFile.Add(new VaultDriftEntry(rel, note.Id, note.Title));
        }

        return new VaultDriftReport(onlyOnDisk, missingFile);
    }

    public async Task<VaultDriftImportResult> ImportDiskOnlyAsync(CancellationToken ct = default)
    {
        var vault = RequireVaultPath();
        var report = await GetReportAsync(ct);
        var messages = new List<string>();
        var imported = 0;

        foreach (var entry in report.OnlyOnDisk)
        {
            ct.ThrowIfCancellationRequested();
            var full = Path.Combine(vault, entry.RelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                messages.Add($"رد شد (فایل نیست): {entry.RelPath}");
                continue;
            }

            await _notes.UpsertFromFileAsync(full, ct);
            imported++;
            messages.Add($"وارد شد از دیسک: {entry.RelPath}");
        }

        return new VaultDriftImportResult(imported, messages);
    }

    private string RequireVaultPath()
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            throw new InvalidOperationException("مسیر خزانه تنظیم نشده است.");
        return vault;
    }

    private async Task<Dictionary<string, long>> LoadKnownRelPathsAsync(CancellationToken ct)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<NoteRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, title AS Title, rel_path AS RelPath
                  FROM notes
                 WHERE rel_path IS NOT NULL
                   AND TRIM(rel_path) <> ''
                """,
                cancellationToken: ct));

        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var rel = row.RelPath!.Replace('\\', '/');
            map.TryAdd(rel, row.Id);
        }

        return map;
    }

    private sealed class NoteRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string? RelPath { get; set; }
    }
}
