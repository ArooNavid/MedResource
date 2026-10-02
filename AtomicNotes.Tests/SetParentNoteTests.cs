using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class SetParentNoteTests
{
    [Fact]
    public async Task SetParent_updates_depth_without_moving_vault_file()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-parent-" + Guid.NewGuid().ToString("N"));
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
            Username = "tree",
            DisplayName = "tree",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var parent = await notes.CreateAsync(owner, "والد", "p", null, Array.Empty<string>());
        var child = await notes.CreateAsync(owner, "فرزند", "c", null, Array.Empty<string>());
        var relBefore = child.RelPath;

        var updated = await notes.SetParentAsync(child.Id, parent.Id);
        Assert.Equal(parent.Id, updated.ParentNoteId);
        Assert.Equal(2, updated.Depth);
        Assert.Equal(relBefore, updated.RelPath);
    }
}
