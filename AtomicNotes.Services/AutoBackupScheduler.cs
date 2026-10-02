using AtomicNotes.Core.Interfaces;

namespace AtomicNotes.Services;

/// <summary>
/// Runs auto-backup on the interval stored in settings.
/// RestartAsync is the only way to apply a new interval. There is no synchronous Restart().
/// </summary>
public sealed class AutoBackupScheduler : IAutoBackupScheduler
{
    private readonly ISettingsService _settings;
    private readonly IBackupService _backup;
    private readonly TimeSpan? _periodOverride;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public AutoBackupScheduler(ISettingsService settings, IBackupService backup)
        : this(settings, backup, periodOverride: null)
    {
    }

    internal AutoBackupScheduler(ISettingsService settings, IBackupService backup, TimeSpan? periodOverride)
    {
        _settings = settings;
        _backup = backup;
        _periodOverride = periodOverride;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
                return Task.CompletedTask;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = RunAsync(_cts.Token);
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null)
            return;

        cts.Cancel();
        try
        {
            if (loop is not null)
                await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The schedule loop exits when it is stopped or restarted.
        }

        cts.Dispose();
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await StartAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var period = _periodOverride
            ?? TimeSpan.FromHours(Math.Max(1, settings.AutoBackupIntervalHours));

        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (!current.AutoBackupEnabled)
                    continue;

                await _backup.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync / RestartAsync.
        }
    }
}
