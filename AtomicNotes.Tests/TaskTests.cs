using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;

namespace AtomicNotes.Tests;

public sealed class TaskTests
{
    [Fact]
    public async Task Tasks_list_open_items_and_toggle_updates_note_content()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-tasks-" + Guid.NewGuid().ToString("N"));
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
            Username = "tasks",
            DisplayName = "tasks",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });
        var tags = new TagService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, new NoteLinkService(database.Factory), database.Stats, new VaultWriteGuard());
        var tasks = new TaskService(notes, tags);

        var note = await notes.CreateAsync(owner, "کارها", """
            ## کارها

            - [ ] تماس با تیم
            - [x] ایمیل زدن
            - [ ] مرور یادداشت
            """, null, Array.Empty<string>());

        var open = await tasks.ListAsync(openOnly: true);
        Assert.Equal(2, open.OpenCount);
        Assert.Equal(1, open.DoneCount);
        Assert.Equal(2, open.Items.Count);
        Assert.All(open.Items, item => Assert.False(item.IsDone));

        var target = open.Items.First(item => item.Text.Contains("تماس"));
        var updated = await tasks.ToggleAsync(note.Id, owner, target.LineIndex, done: true);
        Assert.Contains("- [x] تماس با تیم", updated.Content);

        var after = await tasks.ListAsync(openOnly: true);
        Assert.Equal(1, after.OpenCount);
        Assert.Equal(2, after.DoneCount);
    }

    [Fact]
    public void ToggleLine_rejects_a_non_checkbox_line()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MarkdownTasks.ToggleLine("## عنوان\n\n- فقط بولت", 0, true));
        Assert.Equal("این خط چک‌لیست نیست.", error.Message);
    }
}
