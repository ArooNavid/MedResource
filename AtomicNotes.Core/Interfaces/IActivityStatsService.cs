using AtomicNotes.Core.Models;

namespace AtomicNotes.Core.Interfaces;

public interface IActivityStatsService
{
    Task RecordLoginAsync(long userId, CancellationToken cancellationToken = default);

    Task RecordLogoutAsync(long userId, CancellationToken cancellationToken = default);

    Task IncrementDailyCountAsync(long userId, DailyCountType type, CancellationToken cancellationToken = default);

    /// <summary>Today's counters for the requesting user only.</summary>
    Task<UserDailyStats> GetTodayStatsAsync(long requesterUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A regular user may request only their own id. An admin may request any user.
    /// </summary>
    Task<UserActivityReport> GetReportAsync(
        long requesterUserId,
        long targetUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only comparison of every user's activity in the range.</summary>
    Task<IReadOnlyList<UserComparisonRow>> GetUserComparisonAsync(
        long requesterUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(
        long requesterUserId,
        long targetUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
