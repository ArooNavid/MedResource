using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class CreateFromLinkTargetTests
{
    [Fact]
    public async Task CreateFromLinkTarget_creates_a_note_and_resolves_dangling_links()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-link-create-" + Guid.NewGuid().ToString("N"));
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
        var (notes, links, _, _, _) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var owner = await database.Users.CreateAsync(new User
        {
            Username = "link",
            DisplayName = "link",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        await notes.CreateAsync(owner, "منبع", "[[هدف تازه]]", null, Array.Empty<string>());
        Assert.Single(await links.ListUnresolvedAsync());

        var created = await notes.CreateFromLinkTargetAsync(owner, "هدف تازه");
        Assert.Equal("هدف تازه", created.Title);
        Assert.Empty(await links.ListUnresolvedAsync());

        var again = await notes.CreateFromLinkTargetAsync(owner, "هدف تازه");
        Assert.Equal(created.Id, again.Id);
    }
}
