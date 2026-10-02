using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;

namespace AtomicNotes.App;

/// <summary>
/// Dashboard projection shared by the web shell.
/// A regular user gets their own stats. Comparison stays empty unless the user is an admin.
/// </summary>
public sealed class DashboardViewModel
{
    private readonly IUserRepository _users;
    private readonly IActivityStatsService _stats;
    private readonly long _userId;

    public DashboardViewModel(IUserRepository users, IActivityStatsService stats, long userId)
    {
        _users = users;
        _stats = stats;
        _userId = userId;
    }

    public bool IsAdmin { get; private set; }
    public UserDailyStats? OwnToday { get; private set; }
    public List<UserComparisonRow> Comparison { get; } = new();

    public async Task LoadAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(_userId, cancellationToken);
        IsAdmin = user?.Role == UserRole.Admin;
        OwnToday = await _stats.GetTodayStatsAsync(_userId, cancellationToken);
        Comparison.Clear();
        if (!IsAdmin)
            return;

        Comparison.AddRange(await _stats.GetUserComparisonAsync(_userId, fromUtc, toUtc, cancellationToken));
    }
}
