using System.IO.Compression;
using System.Reflection;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.WPF.ViewModels;

namespace AtomicNotes.Tests;

public sealed class SettingsBackupAndSchedulerTests
{
    [Fact]
    public void Canonical_apis_drop_the_rejected_overloads()
    {
        Assert.Null(typeof(ISettingsService).GetMethod("Load"));
        Assert.NotNull(typeof(ISettingsService).GetMethod("LoadAsync"));
        Assert.NotNull(typeof(ISettingsService).GetProperty(nameof(ISettingsService.SettingsFilePath)));

        Assert.Null(typeof(IAutoBackupScheduler).GetMethod("Restart"));
        Assert.NotNull(typeof(IAutoBackupScheduler).GetMethod(nameof(IAutoBackupScheduler.RestartAsync)));
        Assert.Null(typeof(ITehranClockService).GetMethod("Restart"));
        Assert.NotNull(typeof(ITehranClockService).GetMethod(nameof(ITehranClockService.RestartAsync)));

        Assert.DoesNotContain(
            typeof(AutoBackupScheduler).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "Restart");
        Assert.DoesNotContain(
            typeof(TehranClockService).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "Restart");

        Assert.Equal("AtomicNotes.WPF.ViewModels", typeof(BaseViewModel).Namespace);
        Assert.Equal(typeof(BaseViewModel), typeof(DashboardViewModel).BaseType);
        Assert.Equal(typeof(BaseViewModel), typeof(SettingsViewModel).BaseType);
    }

    [Fact]
    public async Task Backup_reads_settings_from_SettingsFilePath_via_LoadAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atomicnotes-backup-{Guid.NewGuid():N}");
        var vault = Path.Combine(root, "vault");
        var backupDir = Path.Combine(root, "backups");
        var databasePath = Path.Combine(root, "data", "atomicnotes.db");
        var settingsPath = Path.Combine(root, "config", "settings.json");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await File.WriteAllTextAsync(Path.Combine(vault, "note.md"), "note-body");
        await File.WriteAllTextAsync(databasePath, "db-marker");

        var decoyPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        await File.WriteAllTextAsync(decoyPath, "decoy-settings-should-not-be-packed");

        try
        {
            var settingsService = new SettingsService(settingsPath);
            await settingsService.SaveAsync(new AppSettings
            {
                VaultPath = vault,
                BackupPath = backupDir,
                DatabasePath = databasePath,
                AutoBackupEnabled = true,
                AutoBackupIntervalHours = 6
            });
            await File.AppendAllTextAsync(settingsPath, "\n");
            var saved = await File.ReadAllTextAsync(settingsService.SettingsFilePath);
            Assert.Contains("note", saved, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(vault, saved);

            var viewModel = new SettingsViewModel(settingsService);
            await viewModel.LoadAsync();
            Assert.Equal(settingsPath, viewModel.SettingsFilePath);
            Assert.Equal(vault, viewModel.VaultPath);
            Assert.Equal(6, viewModel.AutoBackupIntervalHours);

            var backup = new BackupService(settingsService);
            var created = await backup.CreateBackupAsync();
            Assert.True(created.Success, created.Error);

            using var zip = ZipFile.OpenRead(created.FilePath!);
            var settingsEntry = zip.GetEntry("settings.json");
            Assert.NotNull(settingsEntry);
            using var reader = new StreamReader(settingsEntry!.Open());
            var packedSettings = await reader.ReadToEndAsync();
            Assert.Equal(saved.TrimEnd(), packedSettings.TrimEnd());
            Assert.DoesNotContain("decoy-settings", packedSettings);

            var databaseEntry = zip.GetEntry("database.db");
            Assert.NotNull(databaseEntry);
            using var databaseReader = new StreamReader(databaseEntry!.Open());
            Assert.Equal("db-marker", await databaseReader.ReadToEndAsync());
            Assert.NotNull(zip.GetEntry("vault/note.md"));
        }
        finally
        {
            if (File.Exists(decoyPath))
                File.Delete(decoyPath);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scheduler_restarts_with_RestartAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atomicnotes-sched-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var settings = new SettingsService(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(new AppSettings
            {
                VaultPath = root,
                BackupPath = root,
                AutoBackupEnabled = true,
                AutoBackupIntervalHours = 24
            });

            var backup = new CountingBackup();
            var scheduler = new AutoBackupScheduler(settings, backup, TimeSpan.FromMilliseconds(40));
            await scheduler.StartAsync();
            await WaitUntil(() => backup.Calls >= 1);

            var afterFirstLoop = backup.Calls;
            await scheduler.RestartAsync();
            await WaitUntil(() => backup.Calls > afterFirstLoop);
            await scheduler.StopAsync();
            var stoppedAt = backup.Calls;
            await Task.Delay(120);
            Assert.Equal(stoppedAt, backup.Calls);
            scheduler.Dispose();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(15);
        }

        throw new TimeoutException("Condition was not met before the timeout.");
    }

    private sealed class CountingBackup : IBackupService
    {
        public int Calls;

        public Task<BackupResult> CreateBackupAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new BackupResult(true, FilePath: "memory"));
        }

        public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BackupInfo>>(Array.Empty<BackupInfo>());

        public Task<RestoreResult> RestoreBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RestoreResult(true));

        public Task DeleteBackupAsync(BackupInfo backup, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
