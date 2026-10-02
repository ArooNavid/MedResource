using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AtomicNotes.WPF.ViewModels;

/// <summary>
/// Single BaseViewModel. Earlier drafts placed this type in AtomicNotes.ViewModels
/// and AtomicNotes.App.ViewModels; the canonical namespace is AtomicNotes.WPF.ViewModels.
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
