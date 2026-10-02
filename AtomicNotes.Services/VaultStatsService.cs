using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

/// <summary>Stage 45: vault-wide statistics (notes, links, tags, disk files).</summary>
public sealed class VaultStatsService : IVaultStatsService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISettingsService _settings;

    public VaultStatsService(IDbConnectionFactory factory, ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public async Task<VaultStats> GetAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var row = await connection.QuerySingleAsync<CountRow>(
            new CommandDefinition(
                """
                SELECT
                    SUM(CASE WHEN deleted_at IS NULL THEN 1 ELSE 0 END) AS ActiveNotes,
                    SUM(CASE WHEN deleted_at IS NOT NULL THEN 1 ELSE 0 END) AS TrashedNotes,
                    SUM(CASE WHEN deleted_at IS NULL AND pinned = 1 THEN 1 ELSE 0 END) AS PinnedNotes
                  FROM notes
                """,
                cancellationToken: ct));

        var totalLinks = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM note_links", cancellationToken: ct));
        var unresolvedLinks = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                  FROM note_links nl
                  JOIN notes ns ON ns.id = nl.source_note_id
                 WHERE nl.target_note_id IS NULL
                   AND ns.deleted_at IS NULL
                """,
                cancellationToken: ct));
        var tags = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM tags", cancellationToken: ct));
        var aliases = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM note_aliases", cancellationToken: ct));

        var contents = await connection.QueryAsync<string>(
            new CommandDefinition(
                "SELECT content FROM notes WHERE deleted_at IS NULL",
                cancellationToken: ct));
        long totalWords = 0;
        foreach (var content in contents)
            totalWords += CountWords(content);

        var templates = CountTemplatesOnDisk();
        var markdownFiles = CountMarkdownFilesOnDisk();

        return new VaultStats(
            row.ActiveNotes,
            row.TrashedNotes,
            row.PinnedNotes,
            totalLinks,
            unresolvedLinks,
            tags,
            aliases,
            templates,
            markdownFiles,
            totalWords);
    }

    private int CountTemplatesOnDisk()
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            return 0;
        var folder = Path.Combine(vault, AppConstants.TemplatesFolder);
        if (!Directory.Exists(folder))
            return 0;
        return Directory.EnumerateFiles(folder, "*" + AppConstants.MarkdownExtension, SearchOption.TopDirectoryOnly).Count();
    }

    private int CountMarkdownFilesOnDisk()
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
            return 0;

        var count = 0;
        foreach (var fullPath in Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(vault, fullPath).Replace('\\', '/');
            if (AppConstants.IsIgnoredVaultRelativePath(rel))
                continue;
            count++;
        }

        return count;
    }

    internal static long CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private sealed class CountRow
    {
        public int ActiveNotes { get; set; }
        public int TrashedNotes { get; set; }
        public int PinnedNotes { get; set; }
    }
}
