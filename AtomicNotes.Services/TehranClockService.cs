using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;

namespace AtomicNotes.Services;

/// <summary>
/// Canonical Tehran clock. Merges the conversion API (UtcToTehran, yyyy-MM-dd)
/// with a PeriodicTimer loop that is started explicitly and restarted only via RestartAsync.
/// </summary>
public sealed class TehranClockService : ITehranClockService
{
    private static readonly TimeZoneInfo TehranZone = ResolveTimeZone();
    private readonly TimeSpan _tickPeriod;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;
    private Task? _loop;

    public TehranClockService()
        : this(TimeSpan.FromSeconds(1))
    {
    }

    internal TehranClockService(TimeSpan tickPeriod)
    {
        if (tickPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(tickPeriod));
        _tickPeriod = tickPeriod;
    }

    public event EventHandler<DateTime>? Tick;

    public DateTime TehranNow => UtcToTehran(DateTime.UtcNow);

    public string TehranDateString => TehranNow.ToString(AppConstants.TehranDateFormat);

    public DateTime UtcToTehran(DateTime utc)
    {
        var utcSpecified = utc.Kind switch
        {
            DateTimeKind.Local => utc.ToUniversalTime(),
            DateTimeKind.Utc => utc,
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utcSpecified, TehranZone);
    }

    public string FormatTehranDate(DateTime utc) =>
        UtcToTehran(utc).ToString(AppConstants.TehranDateFormat);

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
                return;

            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(_tickPeriod);
            _loop = RunAsync(_timer, _cts.Token);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        PeriodicTimer? timer;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            timer = _timer;
            loop = _loop;
            _cts = null;
            _timer = null;
            _loop = null;
        }

        if (cts is null)
            return;

        cts.Cancel();
        timer?.Dispose();
        try
        {
            if (loop is not null)
                await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The tick loop exits when StopAsync or RestartAsync cancels it.
        }

        cts.Dispose();
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                Tick?.Invoke(this, TehranNow);
        }
        catch (OperationCanceledException)
        {
            // Expected when the timer is stopped or restarted.
        }
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next platform id.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the next platform id.
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Tehran-Fallback",
            TimeSpan.FromHours(3.5),
            "Tehran",
            "Tehran");
    }
}
