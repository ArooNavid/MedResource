using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class VaultStatsTests
{
    [Fact]
    public async Task GetAsync_aggregates_notes_links_and_words()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-stats-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(root, "vault");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(Path.Combine(vault, "templates"));
        await File.WriteAllTextAsync(Path.Combine(vault, "templates", "daily.md"), "---\ntitle: روز\n---\n");
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
            Username = "stats",
            DisplayName = "stats",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var links = new NoteLinkService(database.Factory, new AliasService(database.Factory));
        var stats = new VaultStatsService(database.Factory, settings);

        await notes.CreateAsync(owner, "الف", "یک دو سه", null, Array.Empty<string>());
        var b = await notes.CreateAsync(owner, "ب", "[[ب]] چهار پنج", null, Array.Empty<string>());
        await links.RebuildLinksForNoteAsync((int)b.Id, "[[ب]] چهار پنج");

        var result = await stats.GetAsync();

        Assert.Equal(2, result.ActiveNotes);
        Assert.Equal(0, result.TrashedNotes);
        Assert.Equal(1, result.TotalLinks);
        Assert.Equal(0, result.UnresolvedLinks);
        Assert.Equal(1, result.Templates);
        Assert.Equal(2, result.MarkdownFilesOnDisk);
        Assert.Equal(6, result.TotalWords);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("  یک   دو  ", 2)]
    public void CountWords_matches_whitespace_split(string? text, long expected) =>
        Assert.Equal(expected, VaultStatsService.CountWords(text));
}
