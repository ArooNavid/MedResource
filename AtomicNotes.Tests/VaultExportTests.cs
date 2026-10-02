using System.IO.Compression;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class VaultExportTests
{
    [Fact]
    public async Task ExportMarkdownZip_includes_vault_markdown_files()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-zip-" + Guid.NewGuid().ToString("N"));
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
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "zip",
            DisplayName = "zip",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var note = await notes.CreateAsync(owner, "زیپ", "متن", null, Array.Empty<string>());
        var export = new VaultExportService(settings, database.Clock);
        var (content, fileName) = await export.ExportMarkdownZipAsync();
        Assert.EndsWith(".zip", fileName, StringComparison.OrdinalIgnoreCase);
        using var ms = new MemoryStream(content);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName.Replace('\\', '/') == note.RelPath);
    }
}
