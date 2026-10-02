using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class TrashNoteTests
{
    [Fact]
    public async Task Delete_moves_note_to_trash_and_restore_brings_it_back()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-trash-" + Guid.NewGuid().ToString("N"));
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
        var tags = new TagService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, new NoteLinkService(database.Factory), database.Stats, new VaultWriteGuard());
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "trash",
            DisplayName = "trash",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var note = await notes.CreateAsync(owner, "موقت", "متن", null, Array.Empty<string>());
        var file = Path.Combine(vault, note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file));

        await notes.DeleteAsync(note.Id);
        Assert.DoesNotContain(await notes.ListAsync(), item => item.Id == note.Id);
        Assert.Contains(await notes.ListTrashAsync(), item => item.Id == note.Id);
        Assert.True(File.Exists(file));

        var restored = await notes.RestoreAsync(note.Id);
        Assert.Null(restored.DeletedAt);
        Assert.Contains(await notes.ListAsync(), item => item.Id == note.Id);
    }

    [Fact]
    public async Task Purge_removes_note_and_vault_file()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-purge-" + Guid.NewGuid().ToString("N"));
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
        var tags = new TagService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, new NoteLinkService(database.Factory), database.Stats, new VaultWriteGuard());
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "purge",
            DisplayName = "purge",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var note = await notes.CreateAsync(owner, "حذف", "بدنه", null, Array.Empty<string>());
        var file = Path.Combine(vault, note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        await notes.DeleteAsync(note.Id);
        await notes.PurgeAsync(note.Id);

        Assert.Empty(await notes.ListTrashAsync());
        Assert.False(File.Exists(file));
    }
}
