using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class WikilinkMarkupTests
{
    [Fact]
    public void Format_wraps_trimmed_title()
    {
        Assert.Equal("[[عنوان]]", WikilinkMarkup.Format("عنوان"));
        Assert.Equal("[[عنوان]]", WikilinkMarkup.Format("  عنوان  "));
    }

    [Fact]
    public void Format_rejects_blank_target()
    {
        Assert.Throws<ArgumentException>(() => WikilinkMarkup.Format(""));
        Assert.Throws<ArgumentException>(() => WikilinkMarkup.Format("   "));
    }

    [Fact]
    public async Task Wikilink_for_note_uses_title()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-wikilink-" + Guid.NewGuid().ToString("N"));
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
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "link",
            DisplayName = "link",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var note = await notes.CreateAsync(owner, "هدف پیوند", "متن", null, Array.Empty<string>());

        var markup = WikilinkMarkup.Format(note.Title);

        Assert.Equal("[[هدف پیوند]]", markup);
    }
}
