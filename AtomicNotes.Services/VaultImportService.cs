using System.IO.Compression;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>Stage 44/47: import markdown zip; stage 47 adds dry-run preview and per-file report.</summary>
public sealed class VaultImportService : IVaultImportService
{
    private readonly ISettingsService _settings;
    private readonly NoteService _notes;

    public VaultImportService(ISettingsService settings, NoteService notes)
    {
        _settings = settings;
        _notes = notes;
    }

    public Task<VaultImportResult> PreviewMarkdownZipAsync(Stream zipStream, CancellationToken ct = default) =>
        ProcessZipAsync(zipStream, dryRun: true, ct);

    public Task<VaultImportResult> ImportMarkdownZipAsync(Stream zipStream, CancellationToken ct = default) =>
        ProcessZipAsync(zipStream, dryRun: false, ct);

    private async Task<VaultImportResult> ProcessZipAsync(Stream zipStream, bool dryRun, CancellationToken ct)
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            throw new InvalidOperationException("مسیر خزانه تنظیم نشده است.");
        if (!dryRun)
            Directory.CreateDirectory(vault);
        var vaultRoot = Path.GetFullPath(vault).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var entries = new List<VaultImportEntry>();

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
                entries.Add(new VaultImportEntry(entry.FullName, VaultImportAction.Skipped, "رد شد: پسوند غیر از .md"));
                continue;
            }

            if (AppConstants.IsIgnoredVaultRelativePath(rel))
            {
                skipped++;
                entries.Add(new VaultImportEntry(rel, VaultImportAction.Skipped, "رد شد: مسیر نادیده‌گرفته‌شده"));
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(vault, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                entries.Add(new VaultImportEntry(rel, VaultImportAction.Invalid, "رد شد: مسیر نامعتبر"));
                continue;
            }

            var existing = await _notes.FindByRelPathAsync(rel, ct);
            if (dryRun)
            {
                if (existing is null)
                {
                    imported++;
                    entries.Add(new VaultImportEntry(rel, VaultImportAction.New, "جدید: در پایگاه‌داده ثبت می‌شود"));
                }
                else
                {
                    updated++;
                    entries.Add(new VaultImportEntry(rel, VaultImportAction.Updated, "به‌روزرسانی: یادداشت موجود بازنویسی می‌شود"));
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
            await _notes.UpsertFromFileAsync(full, ct);
            if (existing is null)
            {
                imported++;
                entries.Add(new VaultImportEntry(rel, VaultImportAction.New, "وارد شد (جدید)"));
            }
            else
            {
                updated++;
                entries.Add(new VaultImportEntry(rel, VaultImportAction.Updated, "وارد شد (به‌روزرسانی)"));
            }
        }

        return new VaultImportResult(imported, updated, skipped, dryRun, entries);
    }

    private static string NormalizeZipEntry(string entryName)
    {
        var rel = entryName.Replace('\\', '/').Trim().TrimStart('/');
        if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains(':', StringComparison.Ordinal))
            return string.Empty;
        return rel;
    }
}
