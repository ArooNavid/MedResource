using AtomicNotes.Core;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;
using Dapper;

namespace AtomicNotes.Tests;

public sealed class PinnedNoteTests
{
    [Fact]
    public async Task Pinned_notes_surface_first_and_on_dashboard_list()
    {
        using var database = new ActivityDatabase();
        using var connection = database.Factory.Create();
        var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
        Assert.Equal(AppConstants.CurrentSchemaVersion, version);

        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-pin-" + Guid.NewGuid().ToString("N"));
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
            Username = "pin",
            DisplayName = "pin",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var notes = new NoteService(
            database.Factory,
            settings,
            new TagService(database.Factory),
            new NoteLinkService(database.Factory),
            database.Stats,
            new VaultWriteGuard());

        var older = await notes.CreateAsync(owner, "قدیمی", "a", null, Array.Empty<string>());
        var newer = await notes.CreateAsync(owner, "تازه", "b", null, Array.Empty<string>());
        await notes.SetPinnedAsync(older.Id, pinned: true);

        var list = await notes.ListAsync();
        Assert.Equal(older.Id, list[0].Id);
        Assert.True(list[0].Pinned);

        var pinned = await notes.ListPinnedAsync();
        Assert.Single(pinned);
        Assert.Equal("قدیمی", pinned[0].Title);

        var unpinned = await notes.SetPinnedAsync(older.Id, pinned: false);
        Assert.False(unpinned.Pinned);
        Assert.Empty(await notes.ListPinnedAsync());
        Assert.Equal(newer.Id, (await notes.ListAsync())[0].Id);
    }
}
