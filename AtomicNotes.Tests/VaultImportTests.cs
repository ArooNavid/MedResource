using System.IO.Compression;
using System.Text;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class VaultImportTests
{
    [Fact]
    public async Task ImportMarkdownZip_upserts_notes_from_archive_entries()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-import-" + Guid.NewGuid().ToString("N"));
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
        var import = new VaultImportService(settings, notes);

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("وارد.md");
            await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            await writer.WriteAsync("""
                ---
                title: از zip
                depth: 1
                tags: []
                ---
                بدنه
                """);
        }

        zipStream.Position = 0;
        var result = await import.ImportMarkdownZipAsync(zipStream);
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Updated);
        Assert.False(result.Preview);
        Assert.Contains(result.Entries, entry => entry.Action == VaultImportAction.New);
        var listed = await notes.ListAsync();
        Assert.Contains(listed, note => note.Title == "از zip");
        Assert.True(File.Exists(Path.Combine(vault, "وارد.md")));
    }

    [Fact]
    public async Task PreviewMarkdownZip_does_not_write_files_and_reports_updates()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-import-prev-" + Guid.NewGuid().ToString("N"));
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
        var import = new VaultImportService(settings, notes);
        await File.WriteAllTextAsync(Path.Combine(vault, "قدیم.md"), """
            ---
            title: قدیم
            depth: 1
            tags: []
            ---
            متن
            """);
        await notes.UpsertFromFileAsync(Path.Combine(vault, "قدیم.md"));

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            {
                var entry = archive.CreateEntry("قدیم.md");
                await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                await writer.WriteAsync("""
                    ---
                    title: قدیم
                    depth: 1
                    tags: []
                    ---
                    متن zip
                    """);
            }
            {
                var fresh = archive.CreateEntry("تازه.md");
                await using var freshWriter = new StreamWriter(fresh.Open(), Encoding.UTF8);
                await freshWriter.WriteAsync("""
                    ---
                    title: تازه
                    depth: 1
                    tags: []
                    ---
                    جدید
                    """);
            }
        }

        zipStream.Position = 0;
        var preview = await import.PreviewMarkdownZipAsync(zipStream);
        Assert.True(preview.Preview);
        Assert.Equal(1, preview.Imported);
        Assert.Equal(1, preview.Updated);
        Assert.False(File.Exists(Path.Combine(vault, "تازه.md")));
        var note = (await notes.ListAsync()).Single(n => n.Title == "قدیم");
        Assert.Equal("متن", note.Content);

        zipStream.Position = 0;
        var applied = await import.ImportMarkdownZipAsync(zipStream);
        Assert.False(applied.Preview);
        Assert.Equal(1, applied.Imported);
        Assert.Equal(1, applied.Updated);
        Assert.True(File.Exists(Path.Combine(vault, "تازه.md")));
        note = (await notes.GetAsync(note.Id))!;
        Assert.Equal("متن zip", note.Content);
    }
}
