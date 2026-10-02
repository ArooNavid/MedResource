namespace AtomicNotes.Services;

/// <summary>
/// Stops the vault watcher from re-importing files this process just wrote.
/// </summary>
public sealed class VaultWriteGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _suppressed = new(StringComparer.OrdinalIgnoreCase);

    public void Suppress(string fullPath, TimeSpan? duration = null)
    {
        var until = DateTime.UtcNow.Add(duration ?? TimeSpan.FromSeconds(2));
        lock (_gate)
            _suppressed[Path.GetFullPath(fullPath)] = until;
    }

    public bool IsSuppressed(string fullPath)
    {
        var key = Path.GetFullPath(fullPath);
        lock (_gate)
        {
            if (!_suppressed.TryGetValue(key, out var until))
                return false;
            if (until < DateTime.UtcNow)
            {
                _suppressed.Remove(key);
                return false;
            }
            return true;
        }
    }
}
