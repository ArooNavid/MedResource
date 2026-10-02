using AtomicNotes.Core;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;
using Dapper;

using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class DailyNoteTests
{
    [Fact]
    public async Task Open_creates_one_tehran_day_and_keeps_later_edits()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-daily-" + Guid.NewGuid().ToString("N"));
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
            Username = "daily",
            DisplayName = "daily",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var daily = new DailyNoteService(settings, database.Clock, notes, new TemplateService(settings, database.Clock, notes, tags, aliases, guard));

        var first = await daily.OpenAsync(owner, "2026-10-02");
        Assert.True(first.Created);
        Assert.Equal("2026-10-02", first.Note.Title);
        Assert.Equal("daily/2026-10-02.md", first.RelPath);
        Assert.Contains("[[2026-10-01]]", first.Note.Content);
        Assert.Contains("[[2026-10-03]]", first.Note.Content);
        var noteTags = await tags.GetTagsForNoteAsync((int)first.Note.Id);
        Assert.Contains(noteTags, tag => tag.Name == AppConstants.DailyNoteTag);
        Assert.True(File.Exists(Path.Combine(vault, "daily", "2026-10-02.md")));

        await notes.UpdateAsync(first.Note.Id, owner, first.Note.Title, "ویرایش کاربر در همان روز", new[] { AppConstants.DailyNoteTag });
        var second = await daily.OpenAsync(owner, "2026-10-02");
        Assert.False(second.Created);
        Assert.Equal(first.Note.Id, second.Note.Id);
        Assert.Equal("ویرایش کاربر در همان روز", second.Note.Content);

        using var connection = database.Factory.Create();
        var creates = await connection.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(note_create_count), 0) FROM user_daily_stats WHERE user_id = @Id",
            new { Id = owner });
        Assert.Equal(1, creates);
    }

    [Fact]
    public async Task Open_adopts_an_existing_vault_file_without_replacing_it()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-daily-" + Guid.NewGuid().ToString("N"));
        var vault = Path.Combine(root, "vault");
        Directory.CreateDirectory(Path.Combine(vault, "daily"));
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
            Username = "daily2",
            DisplayName = "daily2",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var daily = new DailyNoteService(settings, database.Clock, notes, new TemplateService(settings, database.Clock, notes, tags, aliases, guard));

        var path = Path.Combine(vault, "daily", "2026-09-01.md");
        await File.WriteAllTextAsync(path, """
            ---
            title: اول مهر
            depth: 1
            tags:
            - سفارشی
            ---

            متن از ابسیدین
            """);

        var adopted = await daily.OpenAsync(owner, "2026-09-01");
        Assert.False(adopted.Created);
        Assert.Equal("اول مهر", adopted.Note.Title);
        Assert.Contains("متن از ابسیدین", adopted.Note.Content);
        Assert.DoesNotContain("## کارها", adopted.Note.Content);
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("متن از ابسیدین", text);
    }

    [Fact]
    public async Task Open_rejects_a_date_that_is_not_tehran_format()
    {
        using var database = new ActivityDatabase();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "atomicnotes-daily-" + Guid.NewGuid().ToString("N") + ".json"));
        settings.Load();
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var daily = new DailyNoteService(settings, database.Clock, notes, new TemplateService(settings, database.Clock, notes, tags, aliases, guard));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => daily.OpenAsync(1, "۱۴۰۵/۰۷/۱۰"));
        Assert.Equal("تاریخ یادداشت روزانه باید به شکل yyyy-MM-dd باشد.", error.Message);
    }

    [Fact]
    public async Task Open_without_a_date_uses_today_in_tehran()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-daily-" + Guid.NewGuid().ToString("N"));
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
            Username = "today",
            DisplayName = "today",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var daily = new DailyNoteService(settings, database.Clock, notes, new TemplateService(settings, database.Clock, notes, tags, aliases, guard));

        var opened = await daily.OpenAsync(owner);
        Assert.Equal(database.Clock.TehranDateString, opened.TehranDate);
        Assert.Equal($"daily/{database.Clock.TehranDateString}.md", opened.RelPath);
        Assert.True(opened.Created);
    }

    [Fact]
    public async Task GetMonth_marks_days_with_notes_and_starts_week_on_saturday()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-journal-" + Guid.NewGuid().ToString("N"));
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
            Username = "journal",
            DisplayName = "journal",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var (notes, _, aliases, tags, guard) = NoteTestFactory.CreateBundle(database.Factory, settings, database.Stats);
        var daily = new DailyNoteService(settings, database.Clock, notes, new TemplateService(settings, database.Clock, notes, tags, aliases, guard));

        await daily.OpenAsync(owner, "2026-10-02");
        Directory.CreateDirectory(Path.Combine(vault, "daily"));
        await File.WriteAllTextAsync(Path.Combine(vault, "daily", "2026-10-05.md"), """
            ---
            title: 2026-10-05
            depth: 1
            tags: []
            ---

            فقط روی دیسک
            """);

        var month = await daily.GetMonthAsync(2026, 10);
        Assert.Equal(5, month.LeadingPadding);
        Assert.Equal(31, month.Days.Count);
        Assert.True(month.Days.Single(day => day.TehranDate == "2026-10-02").HasNote);
        Assert.True(month.Days.Single(day => day.TehranDate == "2026-10-05").HasNote);
        Assert.False(month.Days.Single(day => day.TehranDate == "2026-10-03").HasNote);

        var badMonth = await Assert.ThrowsAsync<InvalidOperationException>(() => daily.GetMonthAsync(2026, 13));
        Assert.Equal("ماه باید بین ۱ تا ۱۲ باشد.", badMonth.Message);
    }
}
