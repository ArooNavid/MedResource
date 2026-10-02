using AtomicNotes.Core.Interfaces;
using Dapper;

namespace AtomicNotes.Services;

/// <summary>Stage 35: alternate names that resolve wikilinks to a note.</summary>
public sealed class AliasService : IAliasService
{
    private readonly IDbConnectionFactory _factory;

    public AliasService(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<string>> GetAliasesForNoteAsync(int noteId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT alias
                  FROM note_aliases
                 WHERE note_id = @Id
                 ORDER BY alias COLLATE NOCASE
                """,
                new { Id = noteId },
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetAliasesForNoteAsync(int noteId, IEnumerable<string> aliases, CancellationToken ct = default)
    {
        var cleaned = aliases
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var connection = _factory.Create();
        foreach (var alias in cleaned)
        {
            var otherNote = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(
                    """
                    SELECT note_id FROM note_aliases
                     WHERE alias = @Alias COLLATE NOCASE AND note_id <> @Id
                     LIMIT 1
                    """,
                    new { Id = noteId, Alias = alias },
                    cancellationToken: ct));
            if (otherNote is not null)
                throw new InvalidOperationException($"نام مستعار «{alias}» قبلاً استفاده شده است.");

            var titleOwner = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(
                    """
                    SELECT id FROM notes
                     WHERE title = @Alias COLLATE NOCASE
                       AND deleted_at IS NULL
                       AND id <> @Id
                     LIMIT 1
                    """,
                    new { Id = noteId, Alias = alias },
                    cancellationToken: ct));
            if (titleOwner is not null)
                throw new InvalidOperationException($"نام مستعار «{alias}» با عنوان یادداشت دیگری یکی است.");
        }

        using var tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM note_aliases WHERE note_id = @Id",
                    new { Id = noteId },
                    tx,
                    cancellationToken: ct));

            foreach (var alias in cleaned)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO note_aliases (note_id, alias) VALUES (@Id, @Alias)",
                        new { Id = noteId, Alias = alias },
                        tx,
                        cancellationToken: ct));
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<long?> ResolveNoteIdAsync(string target, CancellationToken ct = default)
    {
        var trimmed = (target ?? "").Trim();
        if (trimmed.Length == 0)
            return null;

        using var connection = _factory.Create();
        var byTitle = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                """
                SELECT id FROM notes
                 WHERE title = @Target COLLATE NOCASE
                   AND deleted_at IS NULL
                 LIMIT 1
                """,
                new { Target = trimmed },
                cancellationToken: ct));
        if (byTitle is not null)
            return byTitle;

        return await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                """
                SELECT na.note_id
                  FROM note_aliases na
                  JOIN notes n ON n.id = na.note_id
                 WHERE na.alias = @Target COLLATE NOCASE
                   AND n.deleted_at IS NULL
                 LIMIT 1
                """,
                new { Target = trimmed },
                cancellationToken: ct));
    }
}
