using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class DanglingLinkTests
{
    [Fact]
    public async Task ListUnresolved_lists_only_dangling_wikilinks_from_active_notes()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-dangle-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        settings.Load();
        settings.Save(new AppSettings
        {
            VaultPath = Path.Combine(root, "vault"),
            BackupPath = Path.Combine(root, "backups"),
            DatabasePath = database.Factory.DatabasePath,
            BackupIntervalHours = 24,
            Theme = "System",
            NotificationsEnabled = true
        });
        Directory.CreateDirectory(settings.Current.VaultPath);
        var (_, links, _, _, _) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "dangle",
            DisplayName = "dangle",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        await notes.CreateAsync(owner, "الف", "[[موجود]] و [[گم‌شده]]", null, Array.Empty<string>());
        await notes.CreateAsync(owner, "موجود", "هدف", null, Array.Empty<string>());

        var unresolved = await links.ListUnresolvedAsync();
        var row = Assert.Single(unresolved);
        Assert.Equal("گم‌شده", row.RawTarget);
        Assert.Equal("الف", row.SourceTitle);
    }
}
