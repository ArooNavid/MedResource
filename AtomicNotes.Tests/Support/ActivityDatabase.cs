using AtomicNotes.Data;
using AtomicNotes.Data.Repositories;
using AtomicNotes.Data.Services;
using AtomicNotes.Services;

namespace AtomicNotes.Tests.Support;

public sealed class ActivityDatabase : IDisposable
{
    private readonly string _path;

    public ActivityDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"atomicnotes-{Guid.NewGuid():N}.db");
        Factory = new SqliteConnectionFactory(_path);
        new DatabaseMigrator(Factory).MigrateAsync().GetAwaiter().GetResult();
        Clock = new TehranClockService();
        Users = new UserRepository(Factory);
        Stats = new ActivityStatsService(Factory, Clock);
        Vault = new VaultItemRepository(Factory);
        Imports = new ImportOperationRepository(Factory);
    }

    public SqliteConnectionFactory Factory { get; }
    public TehranClockService Clock { get; }
    public UserRepository Users { get; }
    public ActivityStatsService Stats { get; }
    public VaultItemRepository Vault { get; }
    public ImportOperationRepository Imports { get; }

    public void Dispose()
    {
        Clock.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                var candidate = _path + suffix;
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
            catch (IOException)
            {
                // The engine may still be releasing the file.
            }
        }
    }
}
