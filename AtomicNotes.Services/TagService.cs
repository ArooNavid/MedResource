using System.Data;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

public sealed class TagService : ITagService
{
    public static readonly string[] Palette =
    [
        "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899",
        "#06B6D4", "#84CC16", "#F97316", "#6366F1", "#14B8A6", "#D97706"
    ];

    private readonly IDbConnectionFactory _factory;

    public TagService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Tag>> GetAllTagsAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Tag>(
            new CommandDefinition(
                """
                SELECT t.id AS Id, t.name AS Name, t.color_hex AS ColorHex, t.created_at AS CreatedAt,
                       COUNT(nt.note_id) AS NoteCount
                  FROM tags t
                  LEFT JOIN note_tags nt ON nt.tag_id = t.id
                 GROUP BY t.id
                 ORDER BY t.name COLLATE NOCASE
                """,
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        using var tx = connection.BeginTransaction();
        var tag = GetOrCreate(connection, tx, name);
        tx.Commit();
        return await Task.FromResult(tag);
    }

    public async Task UpdateTagColorAsync(int tagId, string colorHex, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE tags SET color_hex = @Color WHERE id = @Id",
                new { Id = tagId, Color = colorHex },
                cancellationToken: ct));
    }

    public async Task DeleteTagAsync(int tagId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM tags WHERE id = @Id", new { Id = tagId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Tag>> GetTagsForNoteAsync(int noteId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Tag>(
            new CommandDefinition(
                """
                SELECT t.id AS Id, t.name AS Name, t.color_hex AS ColorHex, t.created_at AS CreatedAt
                  FROM tags t
                  JOIN note_tags nt ON nt.tag_id = t.id
                 WHERE nt.note_id = @NoteId
                 ORDER BY t.name COLLATE NOCASE
                """,
                new { NoteId = noteId },
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetTagsForNoteAsync(int noteId, IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        var names = tagNames
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var connection = _factory.Create();
        using var tx = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM note_tags WHERE note_id = @NoteId",
                    new { NoteId = noteId },
                    tx,
                    cancellationToken: ct));

            foreach (var name in names)
            {
                var tag = GetOrCreate(connection, tx, name);
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT OR IGNORE INTO note_tags (note_id, tag_id) VALUES (@NoteId, @TagId)",
                        new { NoteId = noteId, TagId = tag.Id },
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

    public async Task<IReadOnlyList<int>> GetNoteIdsByTagsAsync(IEnumerable<int> tagIds, CancellationToken ct = default)
    {
        var ids = tagIds.Distinct().ToArray();
        if (ids.Length == 0)
            return Array.Empty<int>();

        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<int>(
            new CommandDefinition(
                """
                SELECT note_id
                  FROM note_tags
                 WHERE tag_id IN @Ids
                 GROUP BY note_id
                HAVING COUNT(DISTINCT tag_id) = @TagCount
                """,
                new { Ids = ids, TagCount = ids.Length },
                cancellationToken: ct));
        return rows.ToList();
    }

    private static Tag GetOrCreate(IDbConnection connection, IDbTransaction tx, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Tag name cannot be empty.", nameof(name));

        var existing = connection.QuerySingleOrDefault<Tag>(
            """
            SELECT id AS Id, name AS Name, color_hex AS ColorHex, created_at AS CreatedAt
              FROM tags
             WHERE name = @Name COLLATE NOCASE
            """,
            new { Name = trimmed },
            tx);
        if (existing is not null)
            return existing;

        var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM tags", transaction: tx);
        var color = Palette[count % Palette.Length];
        var id = connection.ExecuteScalar<int>(
            """
            INSERT INTO tags (name, color_hex)
            VALUES (@Name, @Color)
            RETURNING id
            """,
            new { Name = trimmed, Color = color },
            tx);

        return new Tag { Id = id, Name = trimmed, ColorHex = color };
    }
}
