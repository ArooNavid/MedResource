using System.Security.Cryptography;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace AtomicNotes.Services;

public sealed class PdfImportService : IPdfImportService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly NoteService _notes;
    private readonly IActivityStatsService _stats;
    private readonly VaultWriteGuard _guard;

    public PdfImportService(
        IDbConnectionFactory factory,
        ISettingsService settings,
        NoteService notes,
        IActivityStatsService stats,
        VaultWriteGuard guard)
    {
        _factory = factory;
        _settings = settings;
        _notes = notes;
        _stats = stats;
        _guard = guard;
    }

    public async Task<ImportOutcome> ImportAsync(long userId, string fileName, Stream pdf, CancellationToken ct = default)
    {
        var bytes = await ReadAllAsync(pdf, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using (var connection = _factory.Create())
        {
            var duplicate = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    SELECT COUNT(1) FROM import_operations
                     WHERE pdf_hash = @Hash AND status = @Status
                    """,
                    new { Hash = hash, Status = AppConstants.ImportStatusCommitted },
                    cancellationToken: ct));
            if (duplicate > 0)
            {
                return new ImportOutcome(true, AppConstants.ImportStatusCommitted, null, 0, "این PDF قبلاً وارد شده است.", true);
            }
        }

        var operationId = Guid.NewGuid().ToString("N");
        var vault = _settings.Current.VaultPath;
        Directory.CreateDirectory(vault);
        var staging = Path.Combine(vault, ".staging", operationId);
        Directory.CreateDirectory(staging);

        long rowId;
        using (var connection = _factory.Create())
        {
            rowId = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    """
                    INSERT INTO import_operations (operation_id, pdf_hash, original_pdf_path, staging_path, status)
                    VALUES (@Op, @Hash, @Name, @Staging, @Status);
                    SELECT last_insert_rowid();
                    """,
                    new
                    {
                        Op = operationId,
                        Hash = hash,
                        Name = fileName,
                        Staging = staging,
                        Status = AppConstants.ImportStatusPrepared
                    },
                    cancellationToken: ct));
        }

        try
        {
            var pages = Extract(bytes);
            var tree = BuildTree(fileName, bytes, pages);
            var written = WriteStaging(staging, tree, fileName);
            await SetStatusAsync(rowId, AppConstants.ImportStatusStaged, staging, null, ct);

            var created = 0;
            foreach (var file in written)
            {
                ct.ThrowIfCancellationRequested();
                var finalRel = Path.GetRelativePath(staging, file.FullPath).Replace('\\', '/');
                var finalFull = Path.Combine(vault, finalRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(finalFull)!);
                _guard.Suppress(finalFull);
                File.Move(file.FullPath, finalFull, overwrite: true);
                await _notes.UpsertFromFileAsync(finalFull, ct);
                created++;
            }

            await SetStatusAsync(rowId, AppConstants.ImportStatusCommitted, staging, written.FirstOrDefault()?.RelPath, ct);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            await _stats.IncrementDailyCountAsync(userId, DailyCountType.PdfImport, ct);
            return new ImportOutcome(true, AppConstants.ImportStatusCommitted, operationId, created, null, false);
        }
        catch (Exception ex)
        {
            await SetStatusAsync(rowId, AppConstants.ImportStatusFailed, staging, null, ct, ex.Message);
            return new ImportOutcome(false, AppConstants.ImportStatusFailed, operationId, 0, ex.Message, false);
        }
    }

    public async Task RecoverStagedAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var rows = (await connection.QueryAsync<(long Id, string Staging)>(
            new CommandDefinition(
                """
                SELECT id, staging_path
                  FROM import_operations
                 WHERE status = @Status
                """,
                new { Status = AppConstants.ImportStatusStaged },
                cancellationToken: ct))).ToList();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Staging) || !Directory.Exists(row.Staging))
            {
                await SetStatusAsync(row.Id, AppConstants.ImportStatusFailed, row.Staging, null, ct, "staging missing");
                continue;
            }

            var vault = _settings.Current.VaultPath;
            foreach (var file in Directory.EnumerateFiles(row.Staging, "*.md", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(row.Staging, file).Replace('\\', '/');
                var dest = Path.Combine(vault, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                _guard.Suppress(dest);
                File.Copy(file, dest, overwrite: true);
                await _notes.UpsertFromFileAsync(dest, ct);
            }

            await SetStatusAsync(row.Id, AppConstants.ImportStatusCommitted, row.Staging, null, ct);
            Directory.Delete(row.Staging, recursive: true);
        }
    }

    private async Task SetStatusAsync(long id, string status, string? staging, string? finalRel, CancellationToken ct, string? error = null)
    {
        using var connection = _factory.Create();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE import_operations
                   SET status = @Status,
                       staging_path = @Staging,
                       final_relative_path = COALESCE(@Final, final_relative_path),
                       error_message = @Error,
                       updated_utc = @Now
                 WHERE id = @Id
                """,
                new
                {
                    Id = id,
                    Status = status,
                    Staging = staging,
                    Final = finalRel,
                    Error = error,
                    Now = DateTime.UtcNow.ToString("o")
                },
                cancellationToken: ct));
    }

    private static List<PageText> Extract(byte[] bytes)
    {
        var pages = new List<PageText>();
        using var document = PdfDocument.Open(bytes);
        foreach (var page in document.GetPages())
            pages.Add(new PageText(page.Number, page.Text ?? ""));
        return pages;
    }

    private static ImportNode BuildTree(string fileName, byte[] bytes, List<PageText> pages)
    {
        var rootTitle = Path.GetFileNameWithoutExtension(fileName);
        var root = new ImportNode(rootTitle, 1, string.Join("\n\n", pages.Select(page => page.Text)));
        using var document = PdfDocument.Open(bytes);
        if (!document.TryGetBookmarks(out var bookmarks))
            return root;

        root = new ImportNode(rootTitle, 1, pages.Count > 0 ? pages[0].Text : "");
        foreach (var child in bookmarks.Roots)
            root.Children.Add(FromBookmark(child, pages, 2));
        return root;
    }

    private static ImportNode FromBookmark(BookmarkNode node, List<PageText> pages, int depth)
    {
        var pageNumber = node is DocumentBookmarkNode documentNode ? documentNode.PageNumber : 0;
        var text = pages.FirstOrDefault(page => page.Number == pageNumber)?.Text ?? "";
        var current = new ImportNode(string.IsNullOrWhiteSpace(node.Title) ? "untitled" : node.Title, depth, text);
        var nextDepth = Math.Min(depth + 1, AppConstants.MaxTreeDepth);
        foreach (var child in node.Children)
            current.Children.Add(FromBookmark(child, pages, nextDepth));
        return current;
    }

    private List<StagedFile> WriteStaging(string staging, ImportNode root, string sourcePdf)
    {
        var files = new List<StagedFile>();
        WriteNode(staging, root, "", sourcePdf, files);
        return files;
    }

    private void WriteNode(string staging, ImportNode node, string parentRelDir, string sourcePdf, List<StagedFile> files)
    {
        var depth = Math.Clamp(node.Depth, 1, AppConstants.MaxTreeDepth);
        var folder = parentRelDir;
        var fileName = MarkdownFiles.SanitizeFileName(node.Title) + AppConstants.MarkdownExtension;
        var rel = string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";
        var full = Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var body = node.Body;
        if (node.Children.Count > 0)
        {
            var links = string.Join("\n", node.Children.Select(child => $"- [[{child.Title}]]"));
            body = string.IsNullOrWhiteSpace(body) ? links : body + "\n\n" + links;
        }

        File.WriteAllText(full, MarkdownFiles.Compose(node.Title, depth, Array.Empty<string>(), body, sourcePdf));
        files.Add(new StagedFile(full, rel));

        var childDir = string.IsNullOrEmpty(folder)
            ? Path.GetFileNameWithoutExtension(fileName)
            : $"{folder}/{Path.GetFileNameWithoutExtension(fileName)}";
        foreach (var child in node.Children)
        {
            var childDepth = depth + 1;
            if (childDepth > AppConstants.MaxTreeDepth)
                childDepth = AppConstants.MaxTreeDepth;
            child.Depth = childDepth;
            WriteNode(staging, child, childDir, sourcePdf, files);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        return memory.ToArray();
    }

    private sealed record PageText(int Number, string Text);
    private sealed record StagedFile(string FullPath, string RelPath);

    private sealed class ImportNode
    {
        public ImportNode(string title, int depth, string body)
        {
            Title = title;
            Depth = depth;
            Body = body;
        }

        public string Title { get; }
        public int Depth { get; set; }
        public string Body { get; }
        public List<ImportNode> Children { get; } = new();
    }
}
