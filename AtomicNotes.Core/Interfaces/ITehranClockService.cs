namespace AtomicNotes.Core.Interfaces;

public interface ITehranClockService : IDisposable
{
    DateTime TehranNow { get; }
    string TehranDateString { get; }
    DateTime UtcToTehran(DateTime utc);
    string FormatTehranDate(DateTime utc);
    event EventHandler<DateTime>? Tick;
}
