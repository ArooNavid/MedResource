using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.WPF.ViewModels;

/// <summary>
/// Shows the signed-in user's own activity. Comparison across users is loaded only for an admin.
/// </summary>
public sealed class DashboardViewModel : BaseViewModel
{
    private readonly IUserRepository _users;
    private readonly IActivityStatsService _stats;
    private readonly long _userId;
    private bool _isAdmin;
    private UserDailyStats? _ownToday;
    private IReadOnlyList<UserComparisonRow> _comparison = Array.Empty<UserComparisonRow>();

    public DashboardViewModel(IUserRepository users, IActivityStatsService stats, long userId)
    {
        _users = users;
        _stats = stats;
        _userId = userId;
    }

    public bool IsAdmin
    {
        get => _isAdmin;
        private set => SetProperty(ref _isAdmin, value);
    }

    public UserDailyStats? OwnToday
    {
        get => _ownToday;
        private set => SetProperty(ref _ownToday, value);
    }

    public IReadOnlyList<UserComparisonRow> Comparison
    {
        get => _comparison;
        private set => SetProperty(ref _comparison, value);
    }

    public async Task LoadAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        var role = await _users.GetRoleAsync(_userId, cancellationToken).ConfigureAwait(false);
        IsAdmin = role == UserRole.Admin;
        OwnToday = await _stats.GetTodayStatsAsync(_userId, cancellationToken).ConfigureAwait(false);

        if (!IsAdmin)
        {
            Comparison = Array.Empty<UserComparisonRow>();
            return;
        }

        var from = fromUtc ?? DateTime.UtcNow.AddYears(-1);
        var to = toUtc ?? DateTime.UtcNow.AddDays(1);
        Comparison = await _stats.GetUserComparisonAsync(_userId, from, to, cancellationToken).ConfigureAwait(false);
    }
}
