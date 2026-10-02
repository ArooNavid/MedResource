using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface ISettingsService
{
    string SettingsFilePath { get; }
    AppSettings Current { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    void Save();
}
