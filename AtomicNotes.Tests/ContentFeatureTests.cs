using AtomicNotes.Core.Models;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;
using AtomicNotes.Tests.Support;
using Dapper;

namespace AtomicNotes.Tests;

public sealed class ContentFeatureTests
{
    [Fact]
    public void Wikilinks_keep_the_target_before_the_alias()
    {
        var targets = WikilinkParser.ExtractTargets("See [[Project Alpha|Alpha]] and [[project alpha]] and [[ ]].");
        Assert.Equal(new[] { "Project Alpha" }, targets);
    }

    [Fact]
    public async Task Search_tags_and_links_follow_the_stage_rules()
    {
        using var database = new ActivityDatabase();
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json"));
        settings.Load();
        settings.Current.VaultPath = Path.GetDirectoryName(database.Factory.DatabasePath)!;
        settings.Current.DatabasePath = database.Factory.DatabasePath;
        Directory.CreateDirectory(settings.Current.VaultPath);

        using (var connection = database.Factory.Create())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO notes (title, content) VALUES ('Alpha', 'a wikilink lives here');
                INSERT INTO notes (title, content) VALUES ('Beta', 'mentions [[Alpha]] and nothing else');
                INSERT INTO notes (title, content) VALUES ('Gamma', 'plain text');
                """);
        }

        var search = new SearchService(database.Factory);
        var hits = await search.SearchAsync("wikilink");
        Assert.Contains(hits, hit => hit.Title == "Alpha" && hit.Snippet.Contains("<b>", StringComparison.Ordinal));

        var tags = new TagService(database.Factory);
        await tags.SetTagsForNoteAsync(1, new[] { "idea", "draft" });
        await tags.SetTagsForNoteAsync(2, new[] { "idea" });
        await tags.SetTagsForNoteAsync(3, new[] { "idea", "draft" });
        var all = await tags.GetAllTagsAsync();
        var idea = all.Single(tag => tag.Name == "idea").Id;
        var draft = all.Single(tag => tag.Name == "draft").Id;
        var both = await tags.GetNoteIdsByTagsAsync(new[] { idea, draft });
        Assert.Equal(new[] { 1, 3 }, both.OrderBy(id => id).ToArray());

        var links = new NoteLinkService(database.Factory);
        await links.RebuildLinksForNoteAsync(2, "mentions [[Alpha]] and [[Missing Note]]");
        var outgoing = await links.GetOutgoingLinksAsync(2);
        Assert.Equal(2, outgoing.Count);
        Assert.Contains(outgoing, link => link.IsResolved && link.TargetTitle == "Alpha");
        Assert.Contains(outgoing, link => !link.IsResolved && link.RawTarget == "Missing Note");

        await links.ResolveLinksForTitleAsync("Missing Note", 3);
        outgoing = await links.GetOutgoingLinksAsync(2);
        Assert.All(outgoing, link => Assert.True(link.IsResolved));

        await links.NullifyLinksForOldTitleAsync("Alpha");
        outgoing = await links.GetOutgoingLinksAsync(2);
        Assert.Contains(outgoing, link => link.RawTarget == "Alpha" && !link.IsResolved);

        var graph = await new GraphService(database.Factory).LoadGraphAsync();
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Edges, edge => edge.IsResolved);

        var layout = new ForceDirectedLayout();
        var energy = layout.Step(graph.Nodes.ToList(), graph.Edges.ToList());
        Assert.True(energy >= 0);
    }

    [Fact]
    public async Task Note_create_rejects_depth_past_15_and_backup_uses_settings_path()
    {
        using var database = new ActivityDatabase();
        var root = Path.Combine(Path.GetTempPath(), "atomicnotes-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        settings.Load();
        var vault = Path.Combine(root, "vault");
        var backups = Path.Combine(root, "backups");
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(backups);
        settings.Save(new AppSettings
        {
            VaultPath = vault,
            BackupPath = backups,
            DatabasePath = database.Factory.DatabasePath,
            BackupIntervalHours = 24,
            Theme = "System",
            NotificationsEnabled = true
        });
        Assert.EndsWith("settings.json", settings.SettingsFilePath.Replace('\\', '/'));
        Assert.Equal(vault, settings.Load().VaultPath);

        var owner = await database.Users.CreateAsync(new User
        {
            Username = "writer",
            DisplayName = "writer",
            PasswordHash = "x",
            Salt = "y",
            Role = UserRole.User
        });

        var guard = new VaultWriteGuard();
        var tags = new TagService(database.Factory);
        var links = new NoteLinkService(database.Factory);
        var notes = new NoteService(database.Factory, settings, tags, links, database.Stats, guard);
        Note? parent = null;
        for (var depth = 1; depth <= 15; depth++)
        {
            parent = await notes.CreateAsync(owner, $"سطح {depth}", $"متن {depth}", parent?.Id, new[] { "tree" });
            Assert.Equal(depth, parent.Depth);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notes.CreateAsync(owner, "خیلی عمیق", "no", parent!.Id, Array.Empty<string>()));

        File.WriteAllText(Path.Combine(vault, "loose.md"), "hello vault");
        var backup = new BackupService(settings, database.Factory, new SearchService(database.Factory));
        var result = await backup.CreateBackupAsync();
        Assert.True(result.Success, result.Error);
        Assert.EndsWith("settings.json", settings.SettingsFilePath.Replace('\\', '/'));
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.FilePath!);
        Assert.Contains(zip.Entries, entry => entry.FullName == "settings.json");
        Assert.Contains(zip.Entries, entry => entry.FullName == "database.db");
        Assert.Contains(zip.Entries, entry => entry.FullName.Replace('\\', '/').StartsWith("vault/", StringComparison.Ordinal));

        settings.Current.LastAutoBackupAt = DateTime.UtcNow;
        settings.Current.BackupIntervalHours = 24;
        settings.Save();
        var scheduler = new BackupSchedulerService(backup, settings);
        scheduler.Start();
        await scheduler.RestartAsync();
        await scheduler.DisposeAsync();
        Assert.Equal(TimeSpan.Zero, BackupSchedulerService.CalculateWait(null, TimeSpan.FromHours(24), DateTime.UtcNow));
    }

    [Fact]
    public void Tehran_clock_is_three_and_a_half_hours_ahead_of_utc()
    {
        using var clock = new TehranClockService();
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var tehran = clock.UtcToTehran(utc);
        Assert.Equal(TimeSpan.FromHours(3.5), tehran - utc);
        Assert.Equal("2026-06-01", clock.FormatTehranDate(utc));
    }
}
