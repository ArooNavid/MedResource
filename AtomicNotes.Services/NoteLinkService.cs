using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

public sealed class NoteLinkService : INoteLinkService
{
    private readonly IDbConnectionFactory _factory;

    public NoteLinkService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task RebuildLinksForNoteAsync(int sourceNoteId, string markdownContent, CancellationToken ct = default)
    {
        var targets = WikilinkParser.ExtractTargets(markdownContent);
        using var connection = _factory.Create();
        using var tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM note_links WHERE source_note_id = @Id",
                    new { Id = sourceNoteId },
                    tx,
                    cancellationToken: ct));

            foreach (var target in targets)
            {
                var targetId = await connection.QuerySingleOrDefaultAsync<int?>(
                    new CommandDefinition(
                        "SELECT id FROM notes WHERE title = @Title COLLATE NOCASE LIMIT 1",
                        new { Title = target },
                        tx,
                        cancellationToken: ct));

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO note_links (source_note_id, target_note_id, raw_target)
                        VALUES (@Source, @Target, @Raw)
                        """,
                        new { Source = sourceNoteId, Target = targetId, Raw = target },
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

    public Task<IReadOnlyList<NoteLink>> GetOutgoingLinksAsync(int noteId, CancellationToken ct = default) =>
        QueryLinksAsync(
            "WHERE nl.source_note_id = @Id ORDER BY COALESCE(nt.title, nl.raw_target) COLLATE NOCASE",
            noteId,
            ct);

    public Task<IReadOnlyList<NoteLink>> GetBacklinksAsync(int noteId, CancellationToken ct = default) =>
        QueryLinksAsync(
            "WHERE nl.target_note_id = @Id ORDER BY ns.title COLLATE NOCASE",
            noteId,
            ct);

    public async Task ResolveLinksForTitleAsync(string noteTitle, int noteId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE note_links
                   SET target_note_id = @NoteId
                 WHERE target_note_id IS NULL
                   AND raw_target = @Title COLLATE NOCASE
                """,
                new { NoteId = noteId, Title = noteTitle },
                cancellationToken: ct));
    }

    public async Task<int> GetBacklinkCountAsync(int noteId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM note_links WHERE target_note_id = @Id",
                new { Id = noteId },
                cancellationToken: ct));
    }

    public async Task NullifyLinksForOldTitleAsync(string oldTitle, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE note_links
                   SET target_note_id = NULL
                 WHERE raw_target = @Title COLLATE NOCASE
                   AND target_note_id IS NOT NULL
                """,
                new { Title = oldTitle },
                cancellationToken: ct));
    }

    private async Task<IReadOnlyList<NoteLink>> QueryLinksAsync(string where, int noteId, CancellationToken ct)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<NoteLink>(
            new CommandDefinition(
                $"""
                SELECT nl.id AS Id,
                       nl.source_note_id AS SourceNoteId,
                       nl.target_note_id AS TargetNoteId,
                       nl.raw_target AS RawTarget,
                       nl.created_at AS CreatedAt,
                       ns.title AS SourceTitle,
                       COALESCE(nt.title, nl.raw_target) AS TargetTitle
                  FROM note_links nl
                  JOIN notes ns ON ns.id = nl.source_note_id
                  LEFT JOIN notes nt ON nt.id = nl.target_note_id
                {where}
                """,
                new { Id = noteId },
                cancellationToken: ct));
        return rows.ToList();
    }
}
