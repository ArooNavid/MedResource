using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class AutoSyncPathTests
{
    [Fact]
    public async Task UpdateAsync_renames_vault_file_when_auto_sync_setting_is_on()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-autopath-" + Guid.NewGuid().ToString("N"));
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
            NotificationsEnabled = true,
            AutoSyncRelPathOnTitleChange = true
        });
        var notes = NoteTestFactory.Create(database.Factory, settings, database.Stats);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "auto",
            DisplayName = "auto",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var note = await notes.CreateAsync(owner, "قدیم", "متن", null, Array.Empty<string>());
        var oldPath = Path.Combine(vault, note.RelPath.Replace('/', Path.DirectorySeparatorChar));
        var saved = await notes.UpdateAsync(note.Id, owner, "نام جدید", "متن", Array.Empty<string>());
        Assert.NotEqual(note.RelPath, saved.RelPath);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(Path.Combine(vault, saved.RelPath.Replace('/', Path.DirectorySeparatorChar))));
    }
}
