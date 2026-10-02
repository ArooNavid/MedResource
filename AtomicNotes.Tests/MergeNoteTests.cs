using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class MergeNoteTests
{
    [Fact]
    public async Task Merge_appends_source_to_target_resolves_links_and_trashes_source()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-merge-" + Guid.NewGuid().ToString("N"));
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
        var (notes, links, _, _, _) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "merge",
            DisplayName = "merge",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var target = await notes.CreateAsync(owner, "اصلی", "بدنهٔ اصلی", null, new[] { "ایده" });
        var source = await notes.CreateAsync(owner, "فرعی", "متن فرعی", null, new[] { "پروژه" }, new[] { "نام کوتاه" });
        var linker = await notes.CreateAsync(owner, "پیوند", "به [[فرعی]]", null, Array.Empty<string>());

        var merged = await notes.MergeAsync(target.Id, source.Id, owner);
        Assert.Contains("بدنهٔ اصلی", merged.Content);
        Assert.Contains("## فرعی", merged.Content);
        Assert.Contains("متن فرعی", merged.Content);
        Assert.Single(await notes.ListTrashAsync());
        Assert.DoesNotContain(await notes.ListAsync(), note => note.Id == source.Id);

        var outgoing = await links.GetOutgoingLinksAsync((int)linker.Id);
        Assert.Contains(outgoing, link => link.IsResolved && link.TargetNoteId == (int)target.Id);
    }
}
