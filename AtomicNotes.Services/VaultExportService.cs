using System.IO.Compression;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;

namespace AtomicNotes.Services;

/// <summary>Stage 42: download the vault markdown tree as a zip archive.</summary>
public sealed class VaultExportService : IVaultExportService
{
    private readonly ISettingsService _settings;
    private readonly ITehranClockService _clock;

    public VaultExportService(ISettingsService settings, ITehranClockService clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public Task<(byte[] Content, string FileName)> ExportMarkdownZipAsync(CancellationToken ct = default)
    {
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
            throw new InvalidOperationException("مسیر خزانه تنظیم نشده است.");

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var fullPath in Directory.EnumerateFiles(vault, "*.md", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(vault, fullPath).Replace('\\', '/');
                if (AppConstants.IsIgnoredVaultRelativePath(rel))
                    continue;

                var entry = archive.CreateEntry(rel, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = File.ReadAllBytes(fullPath);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        var tehranDay = _clock.TehranNow.ToString(AppConstants.TehranDateFormat);
        var fileName = $"atomicnotes-vault-{tehranDay}.zip";
        return Task.FromResult((buffer.ToArray(), fileName));
    }
}
