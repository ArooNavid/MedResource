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
    private readonly IActivityStatsService _stats;
    private readonly VaultWriteGuard _guard;

    public NoteService(
        IDbConnectionFactory factory,
        ISettingsService settings,
        ITagService tags,
        INoteLinkService links,
        IActivityStatsService stats,
        VaultWriteGuard guard)
    {
        _factory = factory;
        _settings = settings;
        _tags = tags;
        _links = links;
        _stats = stats;
        _guard = guard;
    }

    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                """
                SELECT id AS Id, title AS Title, content AS Content, rel_path AS RelPath,
                       depth AS Depth, parent_note_id AS ParentNoteId, owner_user_id AS OwnerUserId,
                       created_at AS CreatedAt, updated_at AS UpdatedAt, pinned AS Pinned
                  FROM notes
                 ORDER BY pinned DESC, updated_at DESC
                """,
                cancellationToken: ct));
        return rows.ToList();
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
            new CommandDefinition(SelectOne + " WHERE rel_path = @Rel", new { Rel = NormalizeRel(relPath) }, cancellationToken: ct));
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
        WriteFile(rel, title, 1, tagList, content ?? "");
        await _stats.IncrementDailyCountAsync(ownerUserId, DailyCountType.NoteCreate, ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> CreateAsync(
        long ownerUserId,
        string title,
        string content,
        long? parentNoteId,
        IEnumerable<string> tags,
        CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length == 0)
            throw new InvalidOperationException("عنوان یادداشت نمی‌تواند خالی باشد.");

        var parent = parentNoteId is null ? null : await GetAsync(parentNoteId.Value, ct);
        var depth = parent is null ? 1 : parent.Depth + 1;
        if (depth > AppConstants.MaxTreeDepth)
            throw new InvalidOperationException($"عمق درخت نمی‌تواند بیشتر از {AppConstants.MaxTreeDepth} باشد.");

        var rel = await UniqueRelPathAsync(title, parent, ct);
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
        await _tags.SetTagsForNoteAsync((int)id, tagList, ct);
        await _links.RebuildLinksForNoteAsync((int)id, content ?? "", ct);
        await _links.ResolveLinksForTitleAsync(title, (int)id, ct);
        WriteFile(rel, title, depth, tagList, content ?? "");
        await _stats.IncrementDailyCountAsync(ownerUserId, DailyCountType.NoteCreate, ct);

        return (await GetAsync(id, ct))!;
    }

    public async Task<Note> UpdateAsync(
        long id,
        long editorUserId,
        string title,
        string content,
        IEnumerable<string> tags,
        CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
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
        await _tags.SetTagsForNoteAsync((int)id, tagList, ct);
        if (!string.Equals(oldTitle, title, StringComparison.OrdinalIgnoreCase))
            await _links.NullifyLinksForOldTitleAsync(oldTitle, ct);
        await _links.RebuildLinksForNoteAsync((int)id, content ?? "", ct);
        await _links.ResolveLinksForTitleAsync(title, (int)id, ct);
        WriteFile(existing.RelPath, title, existing.Depth, tagList, content ?? "");
        return (await GetAsync(id, ct))!;
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
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
                SelectOne + " ORDER BY updated_at DESC LIMIT @Limit",
                new { Limit = limit },
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Note>> ListPinnedAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = await connection.QueryAsync<Note>(
            new CommandDefinition(
                SelectOne + " WHERE pinned = 1 ORDER BY updated_at DESC",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Note> SetPinnedAsync(long id, bool pinned, CancellationToken ct = default)
    {
        _ = await GetAsync(id, ct) ?? throw new InvalidOperationException("یادداشت پیدا نشد.");
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE notes SET pinned = @Pinned WHERE id = @Id",
                new { Id = id, Pinned = pinned ? 1 : 0 },
                cancellationToken: ct));
        return (await GetAsync(id, ct))!;
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
                    "UPDATE notes SET title = @Title, content = @Content, depth = @Depth, updated_at = @Now WHERE id = @Id",
                    new { Id = existingId, Title = title, Content = parsed.Body, Depth = parsed.Depth, Now = DateTime.UtcNow.ToString("o") },
                    cancellationToken: ct));
        }

        await _tags.SetTagsForNoteAsync((int)existingId.Value, parsed.Tags, ct);
        await _links.RebuildLinksForNoteAsync((int)existingId.Value, parsed.Body, ct);
        await _links.ResolveLinksForTitleAsync(title, (int)existingId.Value, ct);
    }

    private void WriteFile(string relPath, string title, int depth, IEnumerable<string> tags, string body)
    {
        var vault = _settings.Current.VaultPath;
        Directory.CreateDirectory(vault);
        var full = Path.Combine(vault, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _guard.Suppress(full);
        File.WriteAllText(full, MarkdownFiles.Compose(title, depth, tags, body));
    }

    public async Task<string> AllocateRelPathAsync(string title, long? parentNoteId, CancellationToken ct = default)
    {
        var parent = parentNoteId is null ? null : await GetAsync(parentNoteId.Value, ct);
        return await UniqueRelPathAsync(title, parent, ct);
    }

    private async Task<string> UniqueRelPathAsync(string title, Note? parent, CancellationToken ct)
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
                   new CommandDefinition("SELECT COUNT(1) FROM notes WHERE rel_path = @Rel", new { Rel = candidate }, cancellationToken: ct)) > 0)
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
               created_at AS CreatedAt, updated_at AS UpdatedAt, pinned AS Pinned
          FROM notes
        """;
}
