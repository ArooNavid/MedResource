using System.Data;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomicNotes.Data;

/// <summary>
/// Versioned migrations via PRAGMA user_version.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IDbConnectionFactory factory, ILogger<DatabaseMigrator>? logger = null)
    {
        _factory = factory;
        _logger = logger ?? NullLogger<DatabaseMigrator>.Instance;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var current = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
        var target = AppConstants.CurrentSchemaVersion;

        if (current == target)
        {
            _logger.LogDebug("Schema is up-to-date at version {Version}.", target);
            return;
        }

        if (current > target)
            throw new InvalidOperationException($"Database schema v{current} is newer than this application (v{target}).");

        _logger.LogInformation("Migrating schema from v{From} to v{To}.", current, target);

        for (var version = current; version < target; version++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextVersion = version + 1;
            using var transaction = connection.BeginTransaction();
            try
            {
                ApplyMigration(connection, transaction, nextVersion);
                await connection.ExecuteAsync($"PRAGMA user_version = {nextVersion};", transaction: transaction);
                transaction.Commit();
                _logger.LogInformation("Migration {Version} applied.", nextVersion);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Migration {Version} failed and was rolled back.", nextVersion);
                throw;
            }
        }
    }

    private static void ApplyMigration(IDbConnection connection, IDbTransaction transaction, int version)
    {
        switch (version)
        {
            case 1: Migration001_BaseSchema(connection, transaction); break;
            case 2: Migration002_UsersAndSessions(connection, transaction); break;
            case 3: Migration003_UserRole(connection, transaction); break;
            case 4: Migration004_PerUserActivity(connection, transaction); break;
            case 5: Migration005_FtsSearch(connection, transaction); break;
            case 6: Migration006_Tags(connection, transaction); break;
            case 7: Migration007_NoteLinks(connection, transaction); break;
            default: throw new InvalidOperationException($"No migration defined for version {version}.");
        }
    }

    private static void Migration001_BaseSchema(IDbConnection connection, IDbTransaction transaction)
    {
        var sql = $"""
            CREATE TABLE vault_items (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                vault_path  TEXT    NOT NULL,
                rel_path    TEXT    NOT NULL,
                item_type   TEXT    NOT NULL CHECK(item_type IN ('note','folder','asset')),
                title       TEXT    NOT NULL DEFAULT '',
                depth       INTEGER NOT NULL CHECK(depth BETWEEN 1 AND {AppConstants.MaxTreeDepth}),
                parent_id   INTEGER REFERENCES vault_items(id) ON DELETE CASCADE,
                created_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                updated_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                UNIQUE(vault_path, rel_path)
            ) STRICT;

            CREATE TABLE notes (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                title           TEXT    NOT NULL DEFAULT '',
                content         TEXT    NOT NULL DEFAULT '',
                rel_path        TEXT    NOT NULL DEFAULT '',
                depth           INTEGER NOT NULL DEFAULT 1 CHECK(depth BETWEEN 1 AND {AppConstants.MaxTreeDepth}),
                parent_note_id  INTEGER REFERENCES notes(id) ON DELETE SET NULL,
                created_at      TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                updated_at      TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            ) STRICT;

            CREATE UNIQUE INDEX idx_notes_rel_path ON notes(rel_path) WHERE rel_path <> '';
            CREATE INDEX idx_notes_title ON notes(title);
            CREATE INDEX idx_notes_parent ON notes(parent_note_id);

            CREATE TABLE import_operations (
                id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                operation_id         TEXT    NOT NULL UNIQUE,
                pdf_hash             TEXT    NOT NULL DEFAULT '',
                original_pdf_path    TEXT    NOT NULL DEFAULT '',
                staging_path         TEXT,
                final_relative_path  TEXT,
                status               TEXT    NOT NULL DEFAULT '{AppConstants.ImportStatusPrepared}'
                                         CHECK(status IN (
                                             '{AppConstants.ImportStatusPrepared}',
                                             '{AppConstants.ImportStatusStaged}',
                                             '{AppConstants.ImportStatusCommitted}',
                                             '{AppConstants.ImportStatusFailed}'
                                         )),
                started_utc          TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                updated_utc          TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                error_message        TEXT
            ) STRICT;

            CREATE TABLE daily_counts (
                tehran_date       TEXT    NOT NULL PRIMARY KEY,
                pdf_import_count  INTEGER NOT NULL DEFAULT 0,
                note_create_count INTEGER NOT NULL DEFAULT 0
            ) STRICT;

            CREATE INDEX idx_vault_items_parent ON vault_items(parent_id);
            CREATE INDEX idx_vault_items_depth  ON vault_items(depth);
            CREATE INDEX idx_import_ops_status  ON import_operations(status);
            CREATE INDEX idx_import_ops_hash    ON import_operations(pdf_hash, status);
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration002_UsersAndSessions(IDbConnection connection, IDbTransaction transaction)
    {
        const string sql = """
            CREATE TABLE users (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                username      TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                password_hash TEXT    NOT NULL,
                salt          TEXT    NOT NULL,
                display_name  TEXT    NOT NULL DEFAULT '',
                vault_path    TEXT    NOT NULL DEFAULT '',
                staging_path  TEXT    NOT NULL DEFAULT '',
                created_at    TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                last_login_at TEXT,
                is_active     INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
            ) STRICT;

            CREATE TABLE sessions (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id    INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token      TEXT    NOT NULL UNIQUE,
                started_at TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                expires_at TEXT    NOT NULL,
                closed_at  TEXT,
                is_active  INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
            ) STRICT;

            CREATE INDEX idx_sessions_user   ON sessions(user_id);
            CREATE INDEX idx_sessions_token  ON sessions(token);
            CREATE INDEX idx_sessions_active ON sessions(is_active, expires_at);

            ALTER TABLE notes ADD COLUMN owner_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration003_UserRole(IDbConnection connection, IDbTransaction transaction)
    {
        var sql = $"""
            ALTER TABLE users ADD COLUMN role TEXT NOT NULL DEFAULT '{AppConstants.RoleUser}'
                CHECK(role IN ('{AppConstants.RoleUser}','{AppConstants.RoleAdmin}'));
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration004_PerUserActivity(IDbConnection connection, IDbTransaction transaction)
    {
        const string sql = """
            CREATE TABLE user_sessions (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id       INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                login_utc     TEXT    NOT NULL,
                logout_utc    TEXT,
                tehran_date   TEXT    NOT NULL,
                duration_secs INTEGER
            ) STRICT;

            CREATE INDEX idx_user_sessions_user_date ON user_sessions(user_id, tehran_date);
            CREATE INDEX idx_user_sessions_login     ON user_sessions(login_utc);

            CREATE TABLE user_daily_stats (
                user_id           INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                tehran_date       TEXT    NOT NULL,
                pdf_import_count  INTEGER NOT NULL DEFAULT 0,
                note_create_count INTEGER NOT NULL DEFAULT 0,
                session_count     INTEGER NOT NULL DEFAULT 0,
                active_secs       INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_id, tehran_date)
            ) STRICT;

            CREATE INDEX idx_user_daily_date ON user_daily_stats(tehran_date);
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration005_FtsSearch(IDbConnection connection, IDbTransaction transaction)
    {
        const string sql = """
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                title,
                content,
                content='notes',
                content_rowid='id'
            );

            INSERT INTO notes_fts(rowid, title, content)
            SELECT id, title, content FROM notes;

            CREATE TRIGGER IF NOT EXISTS notes_ai
            AFTER INSERT ON notes BEGIN
                INSERT INTO notes_fts(rowid, title, content)
                VALUES (new.id, new.title, new.content);
            END;

            CREATE TRIGGER IF NOT EXISTS notes_ad
            AFTER DELETE ON notes BEGIN
                INSERT INTO notes_fts(notes_fts, rowid, title, content)
                VALUES ('delete', old.id, old.title, old.content);
            END;

            CREATE TRIGGER IF NOT EXISTS notes_au
            AFTER UPDATE ON notes BEGIN
                INSERT INTO notes_fts(notes_fts, rowid, title, content)
                VALUES ('delete', old.id, old.title, old.content);
                INSERT INTO notes_fts(rowid, title, content)
                VALUES (new.id, new.title, new.content);
            END;
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration006_Tags(IDbConnection connection, IDbTransaction transaction)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS tags (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                color_hex   TEXT    NOT NULL DEFAULT '#6C757D',
                created_at  TEXT    NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS note_tags (
                note_id     INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
                tag_id      INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
                PRIMARY KEY (note_id, tag_id)
            );

            CREATE INDEX IF NOT EXISTS ix_note_tags_tag_id  ON note_tags(tag_id);
            CREATE INDEX IF NOT EXISTS ix_note_tags_note_id ON note_tags(note_id);
            """;

        connection.Execute(sql, transaction: transaction);
    }

    private static void Migration007_NoteLinks(IDbConnection connection, IDbTransaction transaction)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS note_links (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                source_note_id INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
                target_note_id INTEGER REFERENCES notes(id) ON DELETE SET NULL,
                raw_target     TEXT    NOT NULL,
                created_at     TEXT    NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS ix_note_links_source ON note_links(source_note_id);
            CREATE INDEX IF NOT EXISTS ix_note_links_target ON note_links(target_note_id);
            """;

        connection.Execute(sql, transaction: transaction);
    }
}
