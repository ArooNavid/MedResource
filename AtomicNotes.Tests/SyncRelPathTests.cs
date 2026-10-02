using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class SyncRelPathTests
{
    [Fact]
    public async Task SyncRelPath_renames_vault_file_to_match_title()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-rename-" + Guid.NewGuid().ToString("N"));
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
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats, tags);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "rename",
            DisplayName = "rename",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var note = await notes.CreateAsync(owner, "عنوان اول", "متن", null, Array.Empty<string>());
        var oldPath = Path.Combine(vault, note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(oldPath));

        await notes.UpdateAsync(note.Id, owner, "نام تازه", "متن", Array.Empty<string>());
        var synced = await notes.SyncRelPathToTitleAsync(note.Id);
        Assert.NotEqual(note.RelPath, synced.RelPath);
        Assert.Contains("نام-تازه", synced.RelPath.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(Path.Combine(vault, synced.RelPath.Replace('/', Path.DirectorySeparatorChar))));
    }
}
