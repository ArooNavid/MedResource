using AtomicNotes.Core;
using AtomicNotes.Tests.Support;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AtomicNotes.Tests;

public sealed class SchemaConflictTests
{
    [Fact]
    public async Task Schema_uses_depth_15_committed_status_and_later_tables()
    {
        using var database = new ActivityDatabase();
        using var connection = database.Factory.Create();
        var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
        Assert.Equal(AppConstants.CurrentSchemaVersion, version);

        await connection.ExecuteAsync(
            """
            INSERT INTO vault_items (vault_path, rel_path, item_type, title, depth)
            VALUES ('/vault', 'a.md', 'note', 'deep', 15)
            """);
        await Assert.ThrowsAnyAsync<SqliteException>(() => connection.ExecuteAsync(
            """
            INSERT INTO vault_items (vault_path, rel_path, item_type, title, depth)
            VALUES ('/vault', 'too.md', 'note', 'too deep', 16)
            """));

        await connection.ExecuteAsync(
            """
            INSERT INTO import_operations (operation_id, status)
            VALUES ('ok', 'Committed')
            """);
        await Assert.ThrowsAnyAsync<SqliteException>(() => connection.ExecuteAsync(
            """
            INSERT INTO import_operations (operation_id, status)
            VALUES ('bad', 'Completed')
            """));

        var tables = (await connection.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type IN ('table','view')")).ToHashSet();
        Assert.Contains("notes_fts", tables);
        Assert.Contains("tags", tables);
        Assert.Contains("note_tags", tables);
        Assert.Contains("note_links", tables);
        Assert.Contains("user_daily_stats", tables);
        Assert.Contains("vault_sync", tables);
    }
}
