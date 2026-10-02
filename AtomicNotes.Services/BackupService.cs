using System.IO.Compression;
using System.Text.Json;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

public sealed class BackupService : IBackupService
{
    private readonly ISettingsService _settingsService;

    public BackupService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.BackupPath))
            return new BackupResult(false, Error: "BackupPath is not configured.");

        if (string.IsNullOrWhiteSpace(settings.VaultPath) || !Directory.Exists(settings.VaultPath))
            return new BackupResult(false, Error: "VaultPath does not exist.");

        Directory.CreateDirectory(settings.BackupPath);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"backup_{timestamp}.zip";
        var finalPath = Path.Combine(settings.BackupPath, fileName);
        var tempPath = finalPath + ".tmp";

        try
        {
            await Task.Run(() => BuildZip(tempPath, settings), cancellationToken).ConfigureAwait(false);

            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);

            return new BackupResult(true, FilePath: finalPath);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            return new BackupResult(false, Error: ex.Message);
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.BackupPath) || !Directory.Exists(settings.BackupPath))
            return Array.Empty<BackupInfo>();

        return Directory
            .EnumerateFiles(settings.BackupPath, "backup_*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new BackupInfo(file.FullName, file.Name, file.LastWriteTimeUtc, file.Length))
            .ToList();
    }

    public async Task<RestoreResult> RestoreBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backup.FilePath))
            return new RestoreResult(false, "Backup file not found.");

        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.VaultPath))
            return new RestoreResult(false, "VaultPath is not configured.");

        var safety = await CreateBackupAsync(cancellationToken).ConfigureAwait(false);
        if (!safety.Success)
            return new RestoreResult(false, $"Could not create safety backup: {safety.Error}");

        var tempExtract = Path.Combine(Path.GetTempPath(), $"atomicnotes_restore_{Guid.NewGuid():N}");
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ZipFile.ExtractToDirectory(backup.FilePath, tempExtract, overwriteFiles: true);

                var vaultSource = Path.Combine(tempExtract, "vault");
                if (!Directory.Exists(vaultSource))
                    throw new InvalidOperationException("Backup does not contain a 'vault' folder.");

                CopyDirectory(vaultSource, settings.VaultPath);

                var databaseSource = Path.Combine(tempExtract, "database.db");
                if (File.Exists(databaseSource) && !string.IsNullOrWhiteSpace(settings.DatabasePath))
                {
                    var databaseDirectory = Path.GetDirectoryName(settings.DatabasePath);
                    if (!string.IsNullOrEmpty(databaseDirectory))
                        Directory.CreateDirectory(databaseDirectory);
                    File.Copy(databaseSource, settings.DatabasePath, overwrite: true);
                }

                // Settings stay where SettingsFilePath points. Restore must not replace them,
                // otherwise the current vault and backup locations would be lost.
            }, cancellationToken).ConfigureAwait(false);

            return new RestoreResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, ex.Message);
        }
        finally
        {
            SafeDeleteDirectory(tempExtract);
        }
    }

    public Task DeleteBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(backup.FilePath))
            File.Delete(backup.FilePath);
        return Task.CompletedTask;
    }

    private void BuildZip(string zipPath, AppSettings settings)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(settings.VaultPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(settings.VaultPath, file);
            zip.CreateEntryFromFile(file, Path.Combine("vault", relative).Replace('\\', '/'), CompressionLevel.Optimal);
        }

        if (!string.IsNullOrWhiteSpace(settings.DatabasePath) && File.Exists(settings.DatabasePath))
            zip.CreateEntryFromFile(settings.DatabasePath, "database.db", CompressionLevel.Optimal);

        if (File.Exists(_settingsService.SettingsFilePath))
            zip.CreateEntryFromFile(_settingsService.SettingsFilePath, "settings.json", CompressionLevel.Optimal);

        var manifest = new
        {
            CreatedAt = DateTime.UtcNow,
            VaultPath = settings.VaultPath,
            SettingsFilePath = _settingsService.SettingsFilePath
        };
        var entry = zip.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temporary archive.
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temporary extract folder.
        }
    }
}
