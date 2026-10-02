using System.Data;
using AtomicNotes.Core.Interfaces;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AtomicNotes.Data;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public SqliteConnectionFactory(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = databasePath };
        ConnectionString = builder.ToString();
    }

    public string ConnectionString { get; }

    public IDbConnection Create()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }
}
