namespace AtomicNotes.Core.Interfaces;

/// <summary>
/// Scheduled auto-backup. Interval changes are applied with RestartAsync.
/// </summary>
public interface IAutoBackupScheduler : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    Task RestartAsync();
}
