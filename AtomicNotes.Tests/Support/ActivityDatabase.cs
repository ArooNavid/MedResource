using AtomicNotes.Core.Interfaces;
using AtomicNotes.Data;
using AtomicNotes.Data.Repositories;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;

namespace AtomicNotes.Tests.Support;

public sealed class ActivityDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "atomicnotes-tests", Guid.NewGuid().ToString("N"));

    public ActivityDatabase()
    {
        Directory.CreateDirectory(_directory);
        var dbPath = Path.Combine(_directory, "atomicnotes.db");
        Factory = new SqliteConnectionFactory(dbPath);
        Clock = new TehranClockService();
        Users = new UserRepository(Factory);
        Stats = new ActivityStatsService(Factory, Clock);
        new DatabaseMigrator(Factory).MigrateAsync().GetAwaiter().GetResult();
    }

    public IDbConnectionFactory Factory { get; }
    public TehranClockService Clock { get; }
    public UserRepository Users { get; }
    public ActivityStatsService Stats { get; }

    public void Dispose()
    {
        Clock.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* the test host may still be releasing the file */ }
    }
}
