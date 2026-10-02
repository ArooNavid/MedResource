using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;
using Dapper;

using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class ObsidianSyncTests
{
    [Fact]
    public async Task Sync_pulls_files_pushes_database_edits_and_deletes_missing_files()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-sync-" + Guid.NewGuid().ToString("N"));
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
            Username = "sync",
            DisplayName = "sync",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var sync = new ObsidianSyncService(database.Factory, settings, notes, tags, aliases, guard, database.Clock);

        var external = Path.Combine(vault, "از-ابسیدین.md");
        await File.WriteAllTextAsync(external, """
            ---
            title: از ابسیدین
            depth: 1
            tags:
            - ایده
            created: 2026-10-02
            updated: 2026-10-02
            ---

            متن فایل خارجی و [[هدف]]
            """);

        var first = await sync.SyncAllAsync();
        Assert.Equal(1, first.Pulled);
        var imported = (await notes.ListAsync()).Single(note => note.Title == "از ابسیدین");
        Assert.Contains("متن فایل خارجی", imported.Content);
        var importedTags = await tags.GetTagsForNoteAsync((int)imported.Id);
        Assert.Contains(importedTags, tag => tag.Name == "ایده");

        await File.WriteAllTextAsync(external, """
            ---
            title: از ابسیدین
            depth: 1
            tags:
            - ایده
            - ویرایش
            created: 2026-10-02
            updated: 2026-10-02
            ---

            متن عوض‌شده در فایل
            """);
        var pulled = await sync.SyncAllAsync();
        Assert.Equal(1, pulled.Pulled);
        imported = (await notes.GetAsync(imported.Id))!;
        Assert.Contains("عوض‌شده", imported.Content);
        importedTags = await tags.GetTagsForNoteAsync((int)imported.Id);
        Assert.Contains(importedTags, tag => tag.Name == "ویرایش");

        var created = await notes.CreateAsync(owner, "ساخته در برنامه", "بدنهٔ برنامه", null, new[] { "داخلی" });
        var aligned = await sync.SyncAllAsync();
        Assert.Equal(0, aligned.Pushed);
        var written = await File.ReadAllTextAsync(Path.Combine(vault, created.RelPath));
        var document = MarkdownFiles.Parse(written);
        Assert.Equal("ساخته در برنامه", document.Title);
        Assert.Contains(document.Tags, tag => tag == "داخلی");
        Assert.False(string.IsNullOrWhiteSpace(document.Created));
        Assert.False(string.IsNullOrWhiteSpace(document.Updated));

        using (var connection = database.Factory.Create())
        {
            await connection.ExecuteAsync(
                "UPDATE notes SET content = @Content, updated_at = @Now WHERE id = @Id",
                new { Id = created.Id, Content = "ویرایش فقط در پایگاه", Now = DateTime.UtcNow.AddMinutes(5).ToString("o") });
        }
        var afterDb = await sync.SyncAllAsync();
        Assert.Equal(1, afterDb.Pushed);
        written = await File.ReadAllTextAsync(Path.Combine(vault, created.RelPath));
        Assert.Contains("ویرایش فقط در پایگاه", written);

        File.Delete(external);
        var afterDelete = await sync.SyncAllAsync();
        Assert.Equal(1, afterDelete.Deleted);
        Assert.Null(await notes.GetAsync(imported.Id));
    }
}
