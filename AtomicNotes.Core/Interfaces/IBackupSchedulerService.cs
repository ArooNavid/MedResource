namespace AtomicNotes.Core.Interfaces;

public interface IBackupSchedulerService : IAsyncDisposable
{
    void Start();
    Task RestartAsync();
}
