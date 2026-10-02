using System.IO.Compression;
using System.Text.Json;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

public sealed class BackupService : IBackupService
{
    private readonly ISettingsService _settings;
    private readonly IDbConnectionFactory _database;
    private readonly ISearchService _search;

    public BackupService(ISettingsService settings, IDbConnectionFactory database, ISearchService search)
    {
        _settings = settings;
        _database = database;
        _search = search;
    }

    public async Task<BackupResult> CreateBackupAsync(CancellationToken ct = default)
    {
        var settings = _settings.Load();
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
            await Task.Run(() => BuildZip(tempPath, settings.VaultPath, timestamp), ct);
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

    public async Task<AutoBackupResult> RunAutoBackupAsync(CancellationToken ct = default)
    {
        var result = await CreateBackupAsync(ct);
        return new AutoBackupResult(result.Success, result.Error, result.FilePath);
    }

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync()
    {
        var settings = _settings.Load();
        if (string.IsNullOrWhiteSpace(settings.BackupPath) || !Directory.Exists(settings.BackupPath))
            return Task.FromResult<IReadOnlyList<BackupInfo>>(Array.Empty<BackupInfo>());

        var files = Directory
            .EnumerateFiles(settings.BackupPath, "backup_*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTime)
            .Select(file => new BackupInfo(file.FullName, file.Name, file.LastWriteTime, file.Length))
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupInfo>>(files);
    }

    public async Task<RestoreResult> RestoreBackupAsync(BackupInfo backup, CancellationToken ct = default)
    {
        if (!File.Exists(backup.FilePath))
            return new RestoreResult(false, "Backup file not found.");

        var settings = _settings.Load();
        if (string.IsNullOrWhiteSpace(settings.VaultPath))
            return new RestoreResult(false, "VaultPath is not configured.");

        var safety = await CreateBackupAsync(ct);
        if (!safety.Success)
            return new RestoreResult(false, $"Could not create safety backup: {safety.Error}");

        var extractDir = Path.Combine(Path.GetTempPath(), $"atomicnotes_restore_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(backup.FilePath, extractDir, overwriteFiles: true);
            ct.ThrowIfCancellationRequested();

            var vaultSource = Path.Combine(extractDir, "vault");
            if (!Directory.Exists(vaultSource))
                throw new InvalidOperationException("Backup archive does not contain a vault folder.");

            CopyDirectory(vaultSource, settings.VaultPath);

            var dbSource = Path.Combine(extractDir, "database.db");
            if (File.Exists(dbSource))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_database.DatabasePath)!);
                File.Copy(dbSource, _database.DatabasePath, overwrite: true);
                await _search.RebuildIndexAsync(ct);
            }

            return new RestoreResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RestoreResult(false, ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the extract directory.
            }
        }
    }

    public Task DeleteBackupAsync(BackupInfo backup)
    {
        if (File.Exists(backup.FilePath))
            File.Delete(backup.FilePath);
        return Task.CompletedTask;
    }

    private void BuildZip(string zipPath, string vaultPath, string timestamp)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var vaultDir = new DirectoryInfo(vaultPath);
        foreach (var file in vaultDir.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}.staging{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            if (string.Equals(file.FullName, _database.DatabasePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(vaultPath, file.FullName);
            zip.CreateEntryFromFile(file.FullName, Path.Combine("vault", relative), CompressionLevel.Optimal);
        }

        if (File.Exists(_database.DatabasePath))
            zip.CreateEntryFromFile(_database.DatabasePath, "database.db", CompressionLevel.Optimal);

        if (File.Exists(_settings.SettingsFilePath))
            zip.CreateEntryFromFile(_settings.SettingsFilePath, "settings.json", CompressionLevel.Optimal);

        var manifest = new
        {
            CreatedAt = DateTime.Now,
            AppVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            VaultPath = vaultPath,
            Timestamp = timestamp
        };
        var entry = zip.CreateEntry("manifest.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A leftover temp zip is preferable to hiding the original error.
        }
    }
}
