using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtomicNotes.Services;

public sealed class VaultWatcherService : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IObsidianSyncService _sync;
    private readonly VaultWriteGuard _guard;
    private readonly ILogger<VaultWatcherService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    public VaultWatcherService(
        ISettingsService settings,
        IObsidianSyncService sync,
        VaultWriteGuard guard,
        ILogger<VaultWatcherService>? logger = null)
    {
        _settings = settings;
        _sync = sync;
        _guard = guard;
        _logger = logger ?? NullLogger<VaultWatcherService>.Instance;
    }

    public void Start()
    {
        Stop();
        var vault = _settings.Current.VaultPath;
        if (string.IsNullOrWhiteSpace(vault))
            return;
        Directory.CreateDirectory(vault);
        _watcher = new FileSystemWatcher(vault, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Schedule(e.OldFullPath);
        Schedule(e.FullPath);
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Schedule(e.FullPath);

    private void OnDeleted(object sender, FileSystemEventArgs e) => Schedule(e.FullPath);

    private void Schedule(string fullPath)
    {
        var vault = _settings.Current.VaultPath;
        if (!string.IsNullOrWhiteSpace(vault)
            && AppConstants.IsIgnoredVaultRelativePath(Path.GetRelativePath(vault, fullPath)))
            return;
        if (_guard.IsSuppressed(fullPath))
            return;

        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_pending.TryGetValue(fullPath, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }
            cts = new CancellationTokenSource();
            _pending[fullPath] = cts;
        }

        _ = DebounceAsync(fullPath, cts);
    }

    private async Task DebounceAsync(string fullPath, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(400, cts.Token);
            if (_guard.IsSuppressed(fullPath))
                return;
            if (File.Exists(fullPath))
                await _sync.PullFileAsync(fullPath, cts.Token);
            else
                await _sync.OnFileMissingAsync(fullPath, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer change replaced this debounce.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sync {Path} from the vault.", fullPath);
        }
        finally
        {
            lock (_gate)
            {
                if (_pending.TryGetValue(fullPath, out var current) && current == cts)
                    _pending.Remove(fullPath);
            }
        }
    }

    public void Stop()
    {
        if (_watcher is null)
            return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();
}
