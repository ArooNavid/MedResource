using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

public sealed class NoteService : INoteService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly ITagService _tags;
    private readonly INoteLinkService _links;
    private readonly IAliasService _aliases;
    private readonly IActivityStatsService _stats;
    private readonly VaultWriteGuard _guard;

    public NoteService(
        IDbConnectionFactory factory,
        ISettingsService settings,
        ITagService tags,
        INoteLinkService links,
        IAliasService aliases,
        IActivityStatsService stats,
        VaultWriteGuard guard)
    {
        _factory = factory;
        _settings = settings;
        _tags = tags;
        _links = links;
        _aliases = aliases;
        _stats = stats;
        _guard = guard;
    }

    public async Task<IReadOnlyList<Note>> ListAsync(
        NoteListSort sort = NoteListSort.Updated,
        bool ascending = false,
        CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE deleted_at IS NULL ORDER BY " + BuildListOrder(sort, ascending),
                cancellationToken: ct));
        return rows.ToList();
    }

    private static string BuildListOrder(NoteListSort sort, bool ascending)
    {
        var dir = ascending ? "ASC" : "DESC";
        var tail = sort switch
        {
            NoteListSort.Title => $"title COLLATE NOCASE {dir}, updated_at DESC",
            NoteListSort.Created => $"created_at {dir}, title COLLATE NOCASE ASC",
            NoteListSort.Depth => $"depth {dir}, title COLLATE NOCASE ASC",
            _ => $"updated_at {dir}, title COLLATE NOCASE ASC"
        };
        return "pinned DESC, " + tail;
    }

    public async Task<Note?> GetRandomAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        return await connection.QuerySingleOrDefaultAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE deleted_at IS NULL ORDER BY RANDOM() LIMIT 1",
                cancellationToken: ct));
    }

    public async Task<Note?> GetAsync(long id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        return await connection.QuerySingleOrDefaultAsync<Note>(
            new CommandDefinition(SelectOne + " WHERE id = @Id", new { Id = id }, cancellationToken: ct));
    }

    public async Task<Note?> FindByRelPathAsync(string relPath, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        return await connection.QuerySingleOrDefaultAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE rel_path = @Rel AND deleted_at IS NULL",
                new { Rel = NormalizeRel(relPath) },
                cancellationToken: ct));
    }

    public async Task<Note> CreateAtPathAsync(
        long ownerUserId,
        string title,
        string content,
        string relPath,
        IEnumerable<string> tags,
        CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان یادداشت نمی‌تواند خالی باشد.");

        var rel = NormalizeRel(relPath);
        if (await FindByRelPathAsync(rel, ct) is not null)
            throw new InvalidOperationException("این مسیر در خزانه وجود دارد.");

        var now = DateTime.UtcNow.ToString("o");
        long id;
        using (var connection = _factory.Create())
        {
            id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO notes (title, content, rel_path, depth, owner_user_id, created_at, updated_at)
                    VALUES (@Title, @Content, @Rel, 1, @Owner, @Now, @Now);
                    SELECT last_insert_rowid();
                    """,
                    new { Title = title, Content = content ?? "", Rel = rel, Owner = ownerUserId, Now = now },
                    cancellationToken: ct));
        }

        var tagList = tags.ToList();
        await _tags.SetTagsForNoteAsync((int)id, tagList, ct);
        await _links.RebuildLinksForNoteAsync((int)id, content ?? "", ct);
        await _links.ResolveLinksForTitleAsync(title, (int)id, ct);
        WriteFile(rel, title, 1, tagList, Array.Empty<string>(), content ?? "");
        await _stats.IncrementDailyCountAsync(ownerUserId, DailyCountType.NoteCreate, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> CreateAsync(
        long ownerUserId,
        string title,
        string content,
        long? parentNoteId,
        IEnumerable<string> tags,
        IEnumerable<string>? aliases = null,
        CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان یادداشت نمی‌تواند خالی باشد.");

        var parent = parentNoteId is null ? null : await RequireActiveAsync(parentNoteId.Value, ct);
        var depth = parent is null ? 1 : parent.Depth + 1;
        if (depth > AppConstants.MaxTreeDepth)
            throw new InvalidOperationException($"عمق درخت نمی‌تواند بیشتر از {AppConstants.MaxTreeDepth} باشد.");

        var rel = await UniqueRelPathAsync(title, parent, excludeNoteId: null, ct);
        var now = DateTime.UtcNow.ToString("o");
        long id;
        using (var connection = _factory.Create())
        {
            id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO notes (title, content, rel_path, depth, parent_note_id, owner_user_id, created_at, updated_at)
                    VALUES (@Title, @Content, @Rel, @Depth, @Parent, @Owner, @Now, @Now);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        Title = title,
                        Content = content ?? "",
                        Rel = rel,
                        Depth = depth,
                        Parent = parent?.Id,
                        Owner = ownerUserId,
                        Now = now
                    },
                    cancellationToken: ct));
        }

        var tagList = tags.ToList();
        var aliasList = (aliases ?? Array.Empty<string>()).ToList();
        await _tags.SetTagsForNoteAsync((int)id, tagList, ct);
        await SaveAliasesAsync((int)id, aliasList, ct);
        await _links.RebuildLinksForNoteAsync((int)id, content ?? "", ct);
        await _links.ResolveLinksForTitleAsync(title, (int)id, ct);
        WriteFile(rel, title, depth, tagList, aliasList, content ?? "");
        await _stats.IncrementDailyCountAsync(ownerUserId, DailyCountType.NoteCreate, ct);

        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> UpdateAsync(
        long id,
        long editorUserId,
        string title,
        string content,
        IEnumerable<string> tags,
        IEnumerable<string>? aliases = null,
        CancellationToken ct = default)
    {
        var existing = await RequireActiveAsync(id, ct);
        title = title.Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان یادداشت نمی‌تواند خالی باشد.");

        var oldTitle = existing.Title;
        var now = DateTime.UtcNow.ToString("o");
        using (var connection = _factory.Create())
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE notes
                       SET title = @Title, content = @Content, updated_at = @Now
                     WHERE id = @Id
                    """,
                    new { Id = id, Title = title, Content = content ?? "", Now = now },
                    cancellationToken: ct));
        }

        var tagList = tags.ToList();
        var aliasList = aliases is null
            ? (await _aliases.GetAliasesForNoteAsync((int)id, ct)).ToList()
            : aliases.ToList();
        await _tags.SetTagsForNoteAsync((int)id, tagList, ct);
        if (aliases is not null)
            await SaveAliasesAsync((int)id, aliasList, ct);
        if (!string.Equals(oldTitle, title, StringComparison.OrdinalIgnoreCase))
            await _links.NullifyLinksForOldTitleAsync(oldTitle, ct);
        await _links.RebuildLinksForNoteAsync((int)id, content ?? "", ct);
        await _links.ResolveLinksForTitleAsync(title, (int)id, ct);
        WriteFile(existing.RelPath, title, existing.Depth, tagList, aliasList, content ?? "");
        return (await GetAsync(id, ct))!;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var existing = await RequireActiveAsync(id, ct);
        var now = DateTime.UtcNow.ToString("o");
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE notes
                   SET deleted_at = @Now, pinned = 0, updated_at = @Now
                 WHERE id = @Id
                """,
                new { Id = id, Now = now },
                cancellationToken: ct));
        _ = existing;
    }

    public async Task<IReadOnlyList<Note>> ListTrashAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE deleted_at IS NOT NULL ORDER BY deleted_at DESC",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Note> RestoreAsync(long id, CancellationToken ct = default)
    {
        var note = await GetAsync(id, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        if (string.IsNullOrWhiteSpace(note.DeletedAt))
            return note;

        var now = DateTime.UtcNow.ToString("o");
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE notes SET deleted_at = NULL, updated_at = @Now WHERE id = @Id",
                new { Id = id, Now = now },
                cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task PurgeAsync(long id, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM notes WHERE id = @Id", new { Id = id }, cancellationToken: ct));

        if (existing is null || string.IsNullOrWhiteSpace(existing.RelPath))
            return;

        var full = Path.Combine(_settings.Current.VaultPath, existing.RelPath);
        _guard.Suppress(full);
        if (File.Exists(full))
            File.Delete(full);
    }

    public async Task<IReadOnlyList<Note>> RecentAsync(int limit, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE deleted_at IS NULL ORDER BY updated_at DESC LIMIT @Limit",
                new { Limit = limit },
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Note>> ListPinnedAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE deleted_at IS NULL AND pinned = 1 ORDER BY updated_at DESC",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Note> SetPinnedAsync(long id, bool pinned, CancellationToken ct = default)
    {
        _ = await RequireActiveAsync(id, ct);
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE notes SET pinned = @Pinned WHERE id = @Id",
                new { Id = id, Pinned = pinned ? 1 : 0 },
                cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> SetParentAsync(long id, long? parentNoteId, CancellationToken ct = default)
    {
        var note = await RequireActiveAsync(id, ct);
        if (parentNoteId == id)
            throw new InvalidOperationException("یادداشت نمی‌تواند والد خودش باشد.");

        Note? parent = null;
        var depth = 1;
        if (parentNoteId is not null)
        {
            parent = await RequireActiveAsync(parentNoteId.Value, ct);
            depth = parent.Depth + 1;
            if (depth > AppConstants.MaxTreeDepth)
                throw new InvalidOperationException($"عمق درخت نمی‌تواند بیشتر از {AppConstants.MaxTreeDepth} باشد.");
        }

        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE notes
                   SET parent_note_id = @Parent, depth = @Depth, updated_at = @Now
                 WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    Parent = parent?.Id,
                    Depth = depth,
                    Now = DateTime.UtcNow.ToString("o")
                },
                cancellationToken: ct));

        var tagNames = (await _tags.GetTagsForNoteAsync((int)id, ct)).Select(tag => tag.Name);
        var aliasNames = await _aliases.GetAliasesForNoteAsync((int)id, ct);
        WriteFile(note.RelPath, note.Title, depth, tagNames, aliasNames, note.Content);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> SyncRelPathToTitleAsync(long id, CancellationToken ct = default)
    {
        var note = await RequireActiveAsync(id, ct);
        var parent = note.ParentNoteId is null ? null : await RequireActiveAsync(note.ParentNoteId.Value, ct);
        var newRel = await UniqueRelPathAsync(note.Title, parent, excludeNoteId: id, ct);
        if (string.Equals(newRel, note.RelPath, StringComparison.OrdinalIgnoreCase))
            return note;

        var tagNames = (await _tags.GetTagsForNoteAsync((int)id, ct)).Select(tag => tag.Name);
        var aliasNames = await _aliases.GetAliasesForNoteAsync((int)id, ct);
        var vault = _settings.Current.VaultPath;
        var oldFull = Path.Combine(vault, note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        var newFull = Path.Combine(vault, newRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
        _guard.Suppress(oldFull);
        _guard.Suppress(newFull);

        if (File.Exists(oldFull))
            File.Move(oldFull, newFull, overwrite: false);
        else
            WriteFile(newRel, note.Title, note.Depth, tagNames, aliasNames, note.Content);

        var now = DateTime.UtcNow.ToString("o");
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE notes SET rel_path = @Rel, updated_at = @Now WHERE id = @Id",
                new { Id = id, Rel = newRel, Now = now },
                cancellationToken: ct));

        if (File.Exists(newFull))
            WriteFile(newRel, note.Title, note.Depth, tagNames, aliasNames, note.Content);

        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> DuplicateAsync(long id, long ownerUserId, CancellationToken ct = default)
    {
        var source = await RequireActiveAsync(id, ct);
        var tagNames = (await _tags.GetTagsForNoteAsync((int)source.Id, ct)).Select(tag => tag.Name);
        var aliasNames = await _aliases.GetAliasesForNoteAsync((int)source.Id, ct);
        var copyTitle = await UniqueCopyTitleAsync(source.Title, ct);
        return await CreateAsync(ownerUserId, copyTitle, source.Content, source.ParentNoteId, tagNames, aliasNames, ct);
    }

    private async Task SaveAliasesAsync(int noteId, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        await _aliases.SetAliasesForNoteAsync(noteId, aliases, ct);
        foreach (var alias in aliases)
            await _links.ResolveLinksForTitleAsync(alias, noteId, ct);
    }

    private async Task<Note> RequireActiveAsync(long id, CancellationToken ct)
    {
        var note = await GetAsync(id, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        if (!string.IsNullOrWhiteSpace(note.DeletedAt))
            throw new InvalidOperationException("این یادداشت در سطل زباله است.");
        return note;
    }

    private async Task<string> UniqueCopyTitleAsync(string title, CancellationToken ct)
    {
        var baseTitle = title.Trim();
        var candidate = $"{baseTitle} — رونوشت";
        using var connection = _factory.Create();
        if (await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT COUNT(1) FROM notes WHERE title = @Title COLLATE NOCASE AND deleted_at IS NULL",
                    new { Title = candidate },
                    cancellationToken: ct)) == 0)
            return candidate;

        var n = 2;
        while (await connection.ExecuteScalarAsync<long>(
                   new CommandDefinition(
                       "SELECT COUNT(1) FROM notes WHERE title = @Title COLLATE NOCASE AND deleted_at IS NULL",
                       new { Title = $"{baseTitle} — رونوشت {n}" },
                       cancellationToken: ct)) > 0)
            n++;

        return $"{baseTitle} — رونوشت {n}";
    }

    public async Task UpsertFromFileAsync(string fullPath, CancellationToken ct = default)
    {
        var vault = _settings.Current.VaultPath;
        var rel = Path.GetRelativePath(vault, fullPath).Replace('\\', '/');
        if (AppConstants.IsIgnoredVaultRelativePath(rel))
            return;

        var markdown = await File.ReadAllTextAsync(fullPath, ct);
        var parsed = MarkdownFiles.Parse(markdown);
        var title = string.IsNullOrWhiteSpace(parsed.Title)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : parsed.Title;

        using var connection = _factory.Create();
        var existingId = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("SELECT id FROM notes WHERE rel_path = @Rel", new { Rel = rel }, cancellationToken: ct));

        if (existingId is null)
        {
            var id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO notes (title, content, rel_path, depth, created_at, updated_at)
                    VALUES (@Title, @Content, @Rel, @Depth, @Now, @Now);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        Title = title,
                        Content = parsed.Body,
                        Rel = rel,
                        Depth = parsed.Depth,
                        Now = DateTime.UtcNow.ToString("o")
                    },
                    cancellationToken: ct));
            existingId = id;
        }
        else
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE notes
                       SET title = @Title, content = @Content, depth = @Depth, updated_at = @Now, deleted_at = NULL
                     WHERE id = @Id
                    """,
                    new { Id = existingId, Title = title, Content = parsed.Body, Depth = parsed.Depth, Now = DateTime.UtcNow.ToString("o") },
                    cancellationToken: ct));
        }

        await _tags.SetTagsForNoteAsync((int)existingId.Value, parsed.Tags, ct);
        await SaveAliasesAsync((int)existingId.Value, parsed.Aliases.ToList(), ct);
        await _links.RebuildLinksForNoteAsync((int)existingId.Value, parsed.Body, ct);
        await _links.ResolveLinksForTitleAsync(title, (int)existingId.Value, ct);
    }

    private void WriteFile(string relPath, string title, int depth, IEnumerable<string> tags, IEnumerable<string> aliases, string body)
    {
        var vault = _settings.Current.VaultPath;
        Directory.CreateDirectory(vault);
        var full = Path.Combine(vault, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _guard.Suppress(full);
        File.WriteAllText(full, MarkdownFiles.Compose(title, depth, tags, body, aliases: aliases));
    }

    public async Task<string> AllocateRelPathAsync(string title, long? parentNoteId, CancellationToken ct = default)
    {
        var parent = parentNoteId is null ? null : await RequireActiveAsync(parentNoteId.Value, ct);
        return await UniqueRelPathAsync(title, parent, excludeNoteId: null, ct);
    }

    private async Task<string> UniqueRelPathAsync(string title, Note? parent, long? excludeNoteId, CancellationToken ct)
    {
        var name = MarkdownFiles.SanitizeFileName(title) + AppConstants.MarkdownExtension;
        var prefix = "";
        if (parent is not null && !string.IsNullOrWhiteSpace(parent.RelPath))
        {
            var parentDir = Path.GetDirectoryName(parent.RelPath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
            var parentName = Path.GetFileNameWithoutExtension(parent.RelPath);
            prefix = Path.Combine(parentDir, parentName).Replace('\\', '/');
        }

        var rel = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
        using var connection = _factory.Create();
        var candidate = rel;
        var n = 2;
        while (await connection.ExecuteScalarAsync<long>(
                   new CommandDefinition(
                       """
                       SELECT COUNT(1) FROM notes
                        WHERE rel_path = @Rel
                          AND (@Exclude IS NULL OR id <> @Exclude)
                       """,
                       new { Rel = candidate, Exclude = excludeNoteId },
                       cancellationToken: ct)) > 0)
        {
            candidate = string.IsNullOrEmpty(prefix)
                ? $"{Path.GetFileNameWithoutExtension(name)}-{n}{AppConstants.MarkdownExtension}"
                : $"{prefix}/{Path.GetFileNameWithoutExtension(name)}-{n}{AppConstants.MarkdownExtension}";
            n++;
        }

        return candidate;
    }

    private static string NormalizeRel(string relPath)
    {
        var rel = (relPath ?? "").Replace('\\', '/').Trim().TrimStart('/');
        if (rel.Length == 0
            || rel.Contains("..", StringComparison.Ordinal)
            || rel.Contains(':', StringComparison.Ordinal)
            || !rel.EndsWith(AppConstants.MarkdownExtension, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(".staging/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("مسیر یادداشت معتبر نیست.");
        return rel;
    }

    private const string SelectOne = """
        SELECT id AS Id, title AS Title, content AS Content, rel_path AS RelPath,
               depth AS Depth, parent_note_id AS ParentNoteId, owner_user_id AS OwnerUserId,
               created_at AS CreatedAt, updated_at AS UpdatedAt, pinned AS Pinned,
               deleted_at AS DeletedAt
          FROM notes
        """;
}
