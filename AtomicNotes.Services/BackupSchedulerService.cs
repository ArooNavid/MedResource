using AtomicNotes.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomicNotes.Services;

public sealed class BackupSchedulerService : IBackupSchedulerService
{
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<BackupSchedulerService> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LoopErrorDelay = TimeSpan.FromMinutes(1);

    public BackupSchedulerService(
        IBackupService backupService,
        ISettingsService settingsService,
        ILogger<BackupSchedulerService>? logger = null)
    {
        _backupService = backupService;
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<BackupSchedulerService>.Instance;
    }

    public void Start()
    {
        _ = StartInternalAsync();
    }

    public Task RestartAsync() => StartInternalAsync();

    private async Task StartInternalAsync()
    {
        await _startLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            _cts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_cts.Token);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var succeeded = false;
            try
            {
                await Task.Run(() => _settingsService.Load(), ct).ConfigureAwait(false);
                var settings = _settingsService.Current;

                if (settings.BackupIntervalHours <= 0)
                {
                    await Task.Delay(PollDelay, ct).ConfigureAwait(false);
                    continue;
                }

                var interval = TimeSpan.FromHours(Math.Max(settings.BackupIntervalHours, MinInterval.TotalHours));
                var wait = CalculateWait(settings.LastAutoBackupAt, interval, DateTime.UtcNow);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct).ConfigureAwait(false);

                var result = await _backupService.RunAutoBackupAsync(ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    _settingsService.Current.LastAutoBackupAt = DateTime.UtcNow;
                    await Task.Run(() => _settingsService.Save(), ct).ConfigureAwait(false);
                    succeeded = true;
                    _logger.LogInformation("Automatic backup written to {Path}.", result.FilePath);
                }
                else
                {
                    _logger.LogWarning("Automatic backup failed: {Error}", result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic backup threw.");
            }

            try
            {
                if (!succeeded)
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup scheduler loop error.");
                try { await Task.Delay(LoopErrorDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    internal static TimeSpan CalculateWait(DateTime? lastAutoBackupAt, TimeSpan interval, DateTime utcNow)
    {
        if (lastAutoBackupAt is null)
            return TimeSpan.Zero;

        var remaining = lastAutoBackupAt.Value + interval - utcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            _loopTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _startLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _startLock.Release();
            _startLock.Dispose();
        }
    }
}
