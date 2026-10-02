using System.Text.RegularExpressions;
using AtomicNotes.Core;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class TemplateTests
{
    [Fact]
    public async Task Template_renders_tehran_placeholders_and_appends_to_a_note()
    {
        using var database = new ActivityDatabase();
        var (settings, notes, templates) = await World(database, "tpl");
        var owner = await User(database, "tpl");

        var created = await templates.CreateAsync("جلسه", "عنوان {{title}} در {{date}} ساعت {{time}}، دیروز {{yesterday}}", new[] { "جلسه" });
        Assert.Equal("جلسه", created.Name);

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => templates.CreateAsync("جلسه", "دوباره", Array.Empty<string>()));
        Assert.Equal("این قالب وجود دارد.", duplicate.Message);

        var note = await notes.CreateAsync(owner, "هفته", "متن قبلی", null, new[] { "ایده" });
        var applied = await templates.ApplyAsync(note.Id, owner, "جلسه");
        Assert.Contains("متن قبلی", applied.Content);
        Assert.Contains("عنوان هفته در " + database.Clock.TehranDateString, applied.Content);
        Assert.DoesNotContain("{{time}}", applied.Content);
        Assert.Matches(new Regex(@"\d{2}:\d{2}"), applied.Content);
        var tags = await new TagService(database.Factory).GetTagsForNoteAsync((int)note.Id);
        Assert.Contains(tags, tag => tag.Name == "ایده");
        Assert.Contains(tags, tag => tag.Name == "جلسه");

        var empty = await notes.CreateAsync(owner, "خالی", "   ", null, Array.Empty<string>());
        var replaced = await templates.ApplyAsync(empty.Id, owner, "جلسه");
        Assert.StartsWith("عنوان خالی", replaced.Content.Trim());
        Assert.DoesNotContain("متن قبلی", replaced.Content);
    }

    [Fact]
    public async Task Daily_note_uses_the_daily_template_when_the_file_exists()
    {
        using var database = new ActivityDatabase();
        var (settings, notes, templates) = await World(database, "daily-tpl");
        var owner = await User(database, "daily-tpl");
        await templates.CreateAsync("daily", "روز {{date}} و [[{{yesterday}}]]", new[] { "جلسه" });
        var daily = new DailyNoteService(settings, database.Clock, notes, templates);

        var opened = await daily.OpenAsync(owner, "2026-10-02");
        Assert.True(opened.Created);
        Assert.Equal("روز 2026-10-02 و [[2026-10-01]]", opened.Note.Content.Trim());
        Assert.DoesNotContain("## کارها", opened.Note.Content);
        var tags = await new TagService(database.Factory).GetTagsForNoteAsync((int)opened.Note.Id);
        Assert.Contains(tags, tag => tag.Name == "جلسه");
        Assert.DoesNotContain(tags, tag => tag.Name == AppConstants.DailyNoteTag);
    }

    [Fact]
    public async Task Sync_leaves_template_files_out_of_the_note_list()
    {
        using var database = new ActivityDatabase();
        var (settings, notes, templates) = await World(database, "sync-tpl");
        await templates.CreateAsync("daily", "قالب {{date}}", new[] { "جلسه" });
        var vault = settings.Current.VaultPath;
        await File.WriteAllTextAsync(Path.Combine(vault, "یادداشت.md"), """
            ---
            title: یادداشت
            depth: 1
            tags: []
            ---

            متن
            """);
        var sync = new ObsidianSyncService(database.Factory, settings, notes, new TagService(database.Factory), new VaultWriteGuard(), database.Clock);
        var report = await sync.SyncAllAsync();
        Assert.Equal(1, report.Pulled);
        var listed = await notes.ListAsync();
        Assert.Single(listed);
        Assert.Equal("یادداشت", listed[0].Title);
        Assert.True(File.Exists(Path.Combine(vault, "templates", "daily.md")));
    }

    private static async Task<long> User(ActivityDatabase database, string name) =>
        await database.Users.CreateAsync(new User
        {
            Username = name,
            DisplayName = name,
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

    private static Task<(SettingsService Settings, NoteService Notes, TemplateService Templates)> World(ActivityDatabase database, string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-templates-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(root, "vault");
        Directory.CreateDirectory(vault);
        var settings = new SettingsService(Path.Combine(root, name + ".json"));
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
        var templates = new TemplateService(settings, database.Clock, notes, tags, new VaultWriteGuard());
        return Task.FromResult((settings, notes, templates));
    }
}
