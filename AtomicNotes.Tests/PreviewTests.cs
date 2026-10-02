using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class PreviewTests
{
    [Fact]
    public async Task Preview_renders_headings_tasks_and_resolved_wikilinks()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-preview-" + Guid.NewGuid().ToString("N"));
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
            Username = "preview",
            DisplayName = "preview",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, links, _, _, _) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var preview = new MarkdownPreviewService(links);

        var target = await notes.CreateAsync(owner, "بتا", "هدف", null, Array.Empty<string>());
        var source = await notes.CreateAsync(owner, "منبع", "پیوند به [[بتا]] و [[نامشخص]]\n\n- [ ] کار", null, Array.Empty<string>());

        var html = await preview.RenderAsync(source.Content, source.Id);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("بتا", html, StringComparison.Ordinal);
        Assert.Contains($"#note-{target.Id}", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#dangling-", html, StringComparison.OrdinalIgnoreCase);
    }
}
