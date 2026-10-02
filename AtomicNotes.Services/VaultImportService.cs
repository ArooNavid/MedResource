using System.IO.Compression;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>Stage 44: import markdown files from a zip into the vault (pairs with stage 42 export).</summary>
public sealed class VaultImportService : IVaultImportService
{
    private readonly ISettingsService _settings;
    private readonly NoteService _notes;

    public VaultImportService(ISettingsService settings, NoteService notes)
    {
        _settings = settings;
        _notes = notes;
    }

    public async Task<VaultImportResult> ImportMarkdownZipAsync(Stream zipStream, CancellationToken ct = default)
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            throw new InvalidOperationException("مسیر خزانه تنظیم نشده است.");
        Directory.CreateDirectory(vault);
        var vaultRoot = Path.GetFullPath(vault).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var imported = 0;
        var skipped = 0;
        var messages = new List<string>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var rel = NormalizeZipEntry(entry.FullName);
            if (rel.Length == 0 || !rel.EndsWith(AppConstants.MarkdownExtension, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (AppConstants.IsIgnoredVaultRelativePath(rel))
            {
                skipped++;
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(vault, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                messages.Add($"رد شد (مسیر نامعتبر): {rel}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
            await _notes.UpsertFromFileAsync(full, ct);
            imported++;
            messages.Add($"وارد شد: {rel}");
        }

        return new VaultImportResult(imported, skipped, messages);
    }

    private static string NormalizeZipEntry(string entryName)
    {
        var rel = entryName.Replace('\\', '/').Trim().TrimStart('/');
        if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains(':', StringComparison.Ordinal))
            return string.Empty;
        return rel;
    }
}
