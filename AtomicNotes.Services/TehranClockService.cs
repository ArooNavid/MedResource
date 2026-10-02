using System.Timers;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;

namespace AtomicNotes.Services;

/// <summary>
/// Canonical Tehran clock: system zone when available, otherwise a fixed +03:30 zone.
/// </summary>
public sealed class TehranClockService : ITehranClockService
{
    private static readonly TimeZoneInfo TimeZone = ResolveTimeZone();
    private readonly System.Timers.Timer _timer;

    public TehranClockService()
    {
        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick?.Invoke(this, TehranNow);
        _timer.Start();
    }

    public DateTime TehranNow => UtcToTehran(DateTime.UtcNow);

    public string TehranDateString => TehranNow.ToString(AppConstants.TehranDateFormat);

    public DateTime UtcToTehran(DateTime utc)
    {
        var utcSpecified = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcSpecified, TimeZone);
    }

    public string FormatTehranDate(DateTime utc) =>
        UtcToTehran(utc).ToString(AppConstants.TehranDateFormat);

    public event EventHandler<DateTime>? Tick;

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try the next id */ }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Tehran-Fallback", TimeSpan.FromHours(3.5), "Tehran", "Tehran");
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
