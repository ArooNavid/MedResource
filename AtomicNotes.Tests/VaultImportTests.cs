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
        var listed = await notes.ListAsync();
        Assert.Contains(listed, note => note.Title == "از zip");
        Assert.True(File.Exists(Path.Combine(vault, "وارد.md")));
    }
}
