using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using Dapper;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class VaultDriftTests
{
    [Fact]
    public async Task GetReportAsync_lists_disk_only_and_missing_files()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-drift-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(root, "vault");
        Directory.CreateDirectory(vault);
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        settings.Load();
        settings.Save(new AppSettings
        {
            VaultPath = vault,
            BackupPath = Path.Combine(root, "backups"),
            DatabasePath = database.Factory.DatabasePath,
            BackupIntervalHours = 24,
            Theme = "System",
            NotificationsEnabled = true
        });
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "drift",
            DisplayName = "drift",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var drift = new VaultDriftService(database.Factory, settings, notes);

        await File.WriteAllTextAsync(Path.Combine(vault, "فقط-دیسک.md"), """
            ---
            title: فقط دیسک
            depth: 1
            tags: []
            ---
            """);

        var ghost = await notes.CreateAsync(owner, "بدون فایل", "متن", null, Array.Empty<string>());
        var written = Path.Combine(vault, ghost.RelPath!.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(written))
            File.Delete(written);
        using (var connection = database.Factory.Create())
        {
            await connection.ExecuteAsync(
                "UPDATE notes SET rel_path = @Rel WHERE id = @Id",
                new { Rel = "حذف‌شده.md", Id = ghost.Id });
        }

        var report = await drift.GetReportAsync();

        var diskOnly = Assert.Single(report.OnlyOnDisk);
        Assert.Equal("فقط-دیسک.md", diskOnly.RelPath);
        var missing = Assert.Single(report.MissingFileOnDisk);
        Assert.Equal("حذف‌شده.md", missing.RelPath);
        Assert.Equal("بدون فایل", missing.Title);
    }

    [Fact]
    public async Task ImportDiskOnlyAsync_indexes_orphan_markdown_files()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-drift-import-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(root, "vault");
        Directory.CreateDirectory(vault);
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        settings.Load();
        settings.Save(new AppSettings
        {
            VaultPath = vault,
            BackupPath = Path.Combine(root, "backups"),
            DatabasePath = database.Factory.DatabasePath,
            BackupIntervalHours = 24,
            Theme = "System",
            NotificationsEnabled = true
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var drift = new VaultDriftService(database.Factory, settings, notes);

        await File.WriteAllTextAsync(Path.Combine(vault, "یتیم.md"), """
            ---
            title: یتیم
            depth: 1
            tags: []
            ---
            بدنه
            """);

        var result = await drift.ImportDiskOnlyAsync();
        Assert.Equal(1, result.Imported);
        var listed = await notes.ListAsync();
        Assert.Contains(listed, note => note.Title == "یتیم");

        var after = await drift.GetReportAsync();
        Assert.Empty(after.OnlyOnDisk);
    }
}
