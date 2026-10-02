using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

/// <summary>
/// Settings are read and written asynchronously.
/// Stage 13 exposed a synchronous Load(); Stage 14 called LoadAsync().
/// The canonical API is LoadAsync/SaveAsync.
/// The file location is always <see cref="SettingsFilePath"/>.
/// </summary>
public interface ISettingsService
{
    string SettingsFilePath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
