using AtomicNotes.Core;
using AtomicNotes.Core.Models;
using AtomicNotes.Data;
using AtomicNotes.Tests.Support;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AtomicNotes.Tests;

public sealed class SchemaConflictTests
{
    [Fact]
    public void MaxTreeDepth_is_15()
    {
        Assert.Equal(15, AppConstants.MaxTreeDepth);
    }

    [Fact]
    public void ImportStatus_uses_Committed_and_not_Completed()
    {
        Assert.Contains(nameof(ImportStatus.Committed), Enum.GetNames<ImportStatus>());
        Assert.DoesNotContain("Completed", Enum.GetNames<ImportStatus>());
        Assert.Equal("Committed", AppConstants.ImportStatusCommitted);
    }

    [Fact]
    public async Task Migration_enforces_depth_role_and_committed_status()
    {
        using var database = new ActivityDatabase();
        using var connection = database.Factory.Create();

        var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
        Assert.Equal(AppConstants.CurrentSchemaVersion, version);

        var schema = string.Join(
            "\n",
            await connection.QueryAsync<string>("SELECT sql FROM sqlite_master WHERE sql IS NOT NULL"));
        Assert.Contains("BETWEEN 1 AND 15", schema);
        Assert.Contains("Committed", schema);
        Assert.DoesNotContain("Completed", schema);
        Assert.Contains("CHECK(role IN ('User','Admin'))", schema);

        await new DatabaseMigrator(database.Factory).MigrateAsync();
        var versionAfter = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
        Assert.Equal(AppConstants.CurrentSchemaVersion, versionAfter);

        await connection.ExecuteAsync(
            "INSERT INTO users (username, password_hash, salt) VALUES ('plain', 'h', 's')");
        var defaultRole = await connection.QuerySingleAsync<string>(
            "SELECT role FROM users WHERE username = 'plain'");
        Assert.Equal(AppConstants.RoleUser, defaultRole);

        Assert.Throws<SqliteException>(() => connection.Execute(
            "INSERT INTO users (username, password_hash, salt, role) VALUES ('bad', 'h', 's', 'Super')"));

        var note = new VaultItem
        {
            VaultPath = "/vault",
            RelPath = "root.md",
            ItemType = "note",
            Title = "Root",
            Depth = AppConstants.MaxTreeDepth
        };
        var id = await database.Vault.InsertAsync(note);
        Assert.True(id > 0);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => database.Vault.InsertAsync(new VaultItem
        {
            VaultPath = "/vault",
            RelPath = "too-deep.md",
            ItemType = "note",
            Title = "Too deep",
            Depth = 16
        }));

        Assert.Throws<SqliteException>(() => connection.Execute(
            """
            INSERT INTO vault_items (vault_path, rel_path, item_type, title, depth)
            VALUES ('/vault', 'raw-too-deep.md', 'note', 'raw', 16)
            """));

        var operationId = await database.Imports.InsertAsync(new ImportOperation
        {
            PdfHash = "abc",
            OriginalPdfPath = "a.pdf",
            Status = ImportStatus.Prepared
        });
        await database.Imports.UpdateStatusAsync(operationId, ImportStatus.Committed);
        Assert.Equal(ImportStatus.Committed, await database.Imports.GetStatusAsync(operationId));

        Assert.Throws<SqliteException>(() => connection.Execute(
            """
            INSERT INTO import_operations (operation_id, status)
            VALUES ('completed-row', 'Completed')
            """));
    }
}
