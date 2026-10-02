using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class DuplicateNoteTests
{
    [Fact]
    public async Task Duplicate_copies_content_tags_and_builds_a_new_vault_file()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-dup-" + Guid.NewGuid().ToString("N"));
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
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "dup",
            DisplayName = "dup",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var tags = new TagService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, new NoteLinkService(database.Factory), database.Stats, new VaultWriteGuard());

        var source = await notes.CreateAsync(owner, "اصلی", "متن [[بتا]]", null, new[] { "ایده" });
        await notes.CreateAsync(owner, "بتا", "هدف", null, Array.Empty<string>());
        var first = await notes.DuplicateAsync(source.Id, owner);
        Assert.Equal("اصلی — رونوشت", first.Title);
        Assert.Equal(source.Content, first.Content);
        Assert.NotEqual(source.Id, first.Id);
        Assert.NotEqual(source.RelPath, first.RelPath);
        Assert.True(File.Exists(Path.Combine(vault, first.RelPath.Replace('/', Path.DirectorySeparatorChar))));
        var copyTags = await tags.GetTagsForNoteAsync((int)first.Id);
        Assert.Contains(copyTags, tag => tag.Name == "ایده");

        var second = await notes.DuplicateAsync(source.Id, owner);
        Assert.Equal("اصلی — رونوشت 2", second.Title);
    }
}
