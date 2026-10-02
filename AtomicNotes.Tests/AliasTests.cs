using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class AliasTests
{
    [Fact]
    public async Task Wikilinks_resolve_through_note_aliases()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-alias-" + Guid.NewGuid().ToString("N"));
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
            Username = "alias",
            DisplayName = "alias",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var target = await notes.CreateAsync(owner, "هدف", "بدنه", null, Array.Empty<string>(), new[] { "نام مستعار" });
        var source = await notes.CreateAsync(owner, "منبع", "پیوند [[نام مستعار]]", null, Array.Empty<string>(), Array.Empty<string>());

        var aliases = new AliasService(database.Factory);
        var links = new NoteLinkService(database.Factory, aliases);
        var outgoing = await links.GetOutgoingLinksAsync((int)source.Id);
        var link = Assert.Single(outgoing);
        Assert.Equal(target.Id, link.TargetNoteId!.Value);
        Assert.True(link.IsResolved);
    }
}
