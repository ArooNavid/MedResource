using AtomicNotes.Core;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class NoteSortTests
{
    [Fact]
    public async Task ListAsync_can_sort_by_title_ascending()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-sort-" + Guid.NewGuid().ToString("N"));
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
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "sort",
            DisplayName = "sort",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        await notes.CreateAsync(owner, "ب", "1", null, Array.Empty<string>());
        await notes.CreateAsync(owner, "ا", "2", null, Array.Empty<string>());

        var sorted = await notes.ListAsync(NoteListSort.Title, ascending: true);
        Assert.Equal("ا", sorted[0].Title);
        Assert.Equal("ب", sorted[1].Title);
    }
}
