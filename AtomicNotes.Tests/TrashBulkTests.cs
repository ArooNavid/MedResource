using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class TrashBulkTests
{
    [Fact]
    public async Task RestoreAll_and_EmptyTrash_work_on_every_trashed_note()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-trash-bulk-" + Guid.NewGuid().ToString("N"));
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
            Username = "bulk",
            DisplayName = "bulk",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var a = await notes.CreateAsync(owner, "الف", "1", null, Array.Empty<string>());
        var b = await notes.CreateAsync(owner, "ب", "2", null, Array.Empty<string>());
        await notes.DeleteAsync(a.Id);
        await notes.DeleteAsync(b.Id);
        Assert.Equal(2, (await notes.ListTrashAsync()).Count);

        Assert.Equal(2, await notes.RestoreAllTrashAsync());
        Assert.Empty(await notes.ListTrashAsync());
        Assert.Equal(2, (await notes.ListAsync()).Count);

        await notes.DeleteAsync(a.Id);
        await notes.DeleteAsync(b.Id);
        var pathA = Path.Combine(vault, a.RelPath.Replace('/', Path.DirectorySeparatorChar));
        var pathB = Path.Combine(vault, b.RelPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));

        Assert.Equal(2, await notes.EmptyTrashAsync());
        Assert.Empty(await notes.ListTrashAsync());
        Assert.False(File.Exists(pathA));
        Assert.False(File.Exists(pathB));
    }
}
