namespace AtomicNotes.Core.Interfaces;

/// <summary>
/// Canonical clock contract. Reconciles the timer loop from 19.md
/// with the conversion API from 17.md. The tick loop is restarted
/// with <see cref="RestartAsync"/>; there is no synchronous Restart().
/// </summary>
public interface ITehranClockService : IDisposable
{
    DateTime TehranNow { get; }

    /// <summary>Current Tehran calendar date, yyyy-MM-dd.</summary>
    string TehranDateString { get; }

    DateTime UtcToTehran(DateTime utc);

    string FormatTehranDate(DateTime utc);

    event EventHandler<DateTime>? Tick;

    void Start();

    Task StopAsync();

    Task RestartAsync();
}
