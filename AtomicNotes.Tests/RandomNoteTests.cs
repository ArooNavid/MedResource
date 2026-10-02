using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class RandomNoteTests
{
    [Fact]
    public async Task GetRandom_returns_an_active_note_from_the_vault()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-random-" + Guid.NewGuid().ToString("N"));
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
            Username = "rand",
            DisplayName = "rand",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        Assert.Null(await notes.GetRandomAsync());

        var first = await notes.CreateAsync(owner, "اول", "a", null, Array.Empty<string>());
        var second = await notes.CreateAsync(owner, "دوم", "b", null, Array.Empty<string>());

        var picked = await notes.GetRandomAsync();
        Assert.NotNull(picked);
        Assert.Contains(picked!.Id, new[] { first.Id, second.Id });

        await notes.DeleteAsync(first.Id);
        picked = await notes.GetRandomAsync();
        Assert.Equal(second.Id, picked!.Id);
    }
}
