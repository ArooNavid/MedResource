using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AtomicNotes.Services;

public sealed class SearchService : ISearchService
{
    private readonly IDbConnectionFactory _factory;

    public SearchService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        var ftsQuery = BuildFtsQuery(query);
        if (ftsQuery.Length == 0)
            return Array.Empty<SearchResult>();

        try
        {
            using var connection = _factory.Create();
            var rows = await connection.QueryAsync<SearchRow>(
                new CommandDefinition(
                    """
                    SELECT n.id AS NoteId,
                           n.title AS Title,
                           snippet(notes_fts, 1, '<b>', '</b>', ' … ', 24) AS Snippet,
                           notes_fts.rank AS Rank
                      FROM notes_fts
                      JOIN notes n ON notes_fts.rowid = n.id
                     WHERE notes_fts MATCH @ftsQuery
                       AND n.deleted_at IS NULL
                     ORDER BY rank
                     LIMIT @limit
                    """,
                    new { ftsQuery, limit },
                    cancellationToken: ct));
            return rows.Select(row => new SearchResult((int)row.NoteId, row.Title, row.Snippet ?? "", row.Rank)).ToList();
        }
        catch (SqliteException ex) when (ex.Message.Contains("fts5", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<SearchResult>();
        }
    }

    public async Task RebuildIndexAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition("INSERT INTO notes_fts(notes_fts) VALUES ('rebuild');", cancellationToken: ct));
    }

    private sealed class SearchRow
    {
        public long NoteId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Snippet { get; set; }
        public double Rank { get; set; }
    }

    public static string BuildFtsQuery(string query)
    {
        var stripped = query;
        foreach (var ch in new[] { '"', '^', '(', ')', '\\', '-', '+', ':' })
            stripped = stripped.Replace(ch, ' ');

        var tokens = stripped
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*")
            .ToArray();

        return tokens.Length == 0 ? string.Empty : string.Join(" AND ", tokens);
    }
}
