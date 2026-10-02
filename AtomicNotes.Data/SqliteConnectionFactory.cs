using System.Data;
using AtomicNotes.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace AtomicNotes.Data;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public IDbConnection Create()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
}
