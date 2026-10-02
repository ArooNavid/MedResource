using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class MarkdownExchangeTests
{
    [Fact]
    public async Task Export_and_import_round_trip_vault_markdown()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-md-" + Guid.NewGuid().ToString("N"));
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
            Username = "md",
            DisplayName = "md",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var tags = new TagService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, new NoteLinkService(database.Factory), database.Stats, new VaultWriteGuard());
        var exchange = new NoteMarkdownService(notes, tags, database.Clock, notes);

        var note = await notes.CreateAsync(owner, "خروجی", "متن **بولد**", null, new[] { "ایده" });
        var exported = await exchange.ExportAsync(note.Id);
        Assert.Contains("title: خروجی", exported.Content);
        Assert.Contains("tags:", exported.Content);
        Assert.Contains("متن **بولد**", exported.Content);
        Assert.EndsWith(".md", exported.FileName);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exported.Content));
        var imported = await exchange.ImportAsync(owner, "تازه.md", stream);
        Assert.Equal("خروجی", imported.Title);
        Assert.Contains("متن **بولد**", imported.Content);
        Assert.True(File.Exists(Path.Combine(vault, imported.RelPath.Replace('/', Path.DirectorySeparatorChar))));
    }
}
