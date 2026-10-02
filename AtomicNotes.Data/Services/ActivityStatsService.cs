using System.Globalization;
using AtomicNotes.Core;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using AtomicNotes.Data.Repositories;
using Dapper;

namespace AtomicNotes.Data.Services;

/// <summary>
/// Persists per-user activity and enforces who may read it.
/// The role is loaded from the users table for the requester; it is never taken from the caller.
/// </summary>
public sealed class ActivityStatsService : IActivityStatsService
{
    private readonly IDbConnectionFactory _factory;
    private readonly ITehranClockService _clock;

    public ActivityStatsService(IDbConnectionFactory factory, ITehranClockService clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task RecordLoginAsync(long userId, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var tehranDate = _clock.FormatTehranDate(nowUtc);

        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO user_sessions (user_id, login_utc, tehran_date)
                VALUES (@UserId, @LoginUtc, @TehranDate)
                """,
                new { UserId = userId, LoginUtc = FormatUtc(nowUtc), TehranDate = tehranDate },
                transaction,
                cancellationToken: cancellationToken));

        await UpsertDailyStatAsync(connection, transaction, userId, tehranDate, sessionCountDelta: 1, cancellationToken: cancellationToken);
        transaction.Commit();
    }

    public async Task RecordLogoutAsync(long userId, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        var session = await connection.QuerySingleOrDefaultAsync<OpenSessionRow>(
            new CommandDefinition(
                """
                SELECT id, login_utc
                  FROM user_sessions
                 WHERE user_id = @UserId AND logout_utc IS NULL
                 ORDER BY login_utc DESC
                 LIMIT 1
                """,
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));

        if (session is null)
        {
            transaction.Rollback();
            return;
        }

        var loginUtc = ParseUtc(session.login_utc);
        var durationSecs = Math.Max(0, (int)(nowUtc - loginUtc).TotalSeconds);
        var tehranDate = _clock.FormatTehranDate(loginUtc);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE user_sessions
                   SET logout_utc = @LogoutUtc,
                       duration_secs = @DurationSecs
                 WHERE id = @Id
                """,
                new { LogoutUtc = FormatUtc(nowUtc), DurationSecs = durationSecs, Id = session.id },
                transaction,
                cancellationToken: cancellationToken));

        await UpsertDailyStatAsync(connection, transaction, userId, tehranDate, activeSecsDelta: durationSecs, cancellationToken: cancellationToken);
        transaction.Commit();
    }

    public async Task IncrementDailyCountAsync(long userId, DailyCountType type, CancellationToken cancellationToken = default)
    {
        var tehranDate = _clock.FormatTehranDate(DateTime.UtcNow);
        var (pdf, note) = type switch
        {
            DailyCountType.PdfImport => (1, 0),
            DailyCountType.NoteCreate => (0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        await UpsertDailyStatAsync(connection, transaction, userId, tehranDate, pdfDelta: pdf, noteDelta: note, cancellationToken: cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO daily_counts (tehran_date, pdf_import_count, note_create_count)
                VALUES (@Date, @Pdf, @Note)
                ON CONFLICT(tehran_date) DO UPDATE SET
                    pdf_import_count  = pdf_import_count  + excluded.pdf_import_count,
                    note_create_count = note_create_count + excluded.note_create_count
                """,
                new { Date = tehranDate, Pdf = pdf, Note = note },
                transaction,
                cancellationToken: cancellationToken));

        transaction.Commit();
    }

    public async Task<UserDailyStats> GetTodayStatsAsync(long requesterUserId, CancellationToken cancellationToken = default)
    {
        var tehranDate = _clock.TehranDateString;
        using var connection = _factory.Create();
        await EnsureUserExistsAsync(connection, requesterUserId, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<DailyRow>(
            new CommandDefinition(
                """
                SELECT pdf_import_count, note_create_count, session_count, active_secs
                  FROM user_daily_stats
                 WHERE user_id = @UserId AND tehran_date = @Date
                """,
                new { UserId = requesterUserId, Date = tehranDate },
                cancellationToken: cancellationToken));

        return new UserDailyStats(
            requesterUserId,
            tehranDate,
            (int)(row?.pdf_import_count ?? 0),
            (int)(row?.note_create_count ?? 0),
            (int)(row?.session_count ?? 0),
            (int)(row?.active_secs ?? 0));
    }

    public async Task<UserActivityReport> GetReportAsync(
        long requesterUserId,
        long targetUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        await EnsureCanViewAsync(connection, requesterUserId, targetUserId, cancellationToken);

        var username = await connection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT username FROM users WHERE id = @Id",
                new { Id = targetUserId },
                cancellationToken: cancellationToken));

        var fromTehran = _clock.FormatTehranDate(fromUtc);
        var toTehran = _clock.FormatTehranDate(toUtc);
        var rows = (await connection.QueryAsync<DailyActivityRow>(
            new CommandDefinition(
                """
                SELECT tehran_date       AS TehranDate,
                       pdf_import_count  AS PdfImports,
                       note_create_count AS NotesCreated,
                       session_count     AS SessionCount,
                       active_secs       AS ActiveSecs
                  FROM user_daily_stats
                 WHERE user_id = @UserId
                   AND tehran_date BETWEEN @From AND @To
                 ORDER BY tehran_date
                """,
                new { UserId = targetUserId, From = fromTehran, To = toTehran },
                cancellationToken: cancellationToken))).ToList();

        var daily = rows.Select(row => new DailyActivity(
            row.TehranDate,
            (int)row.PdfImports,
            (int)row.NotesCreated,
            (int)row.SessionCount,
            (int)row.ActiveSecs)).ToList();

        return new UserActivityReport(
            targetUserId,
            username,
            fromUtc,
            toUtc,
            daily.Sum(row => row.PdfImports),
            daily.Sum(row => row.NotesCreated),
            daily.Sum(row => row.SessionCount),
            daily.Sum(row => row.ActiveSecs),
            daily);
    }

    public async Task<IReadOnlyList<UserComparisonRow>> GetUserComparisonAsync(
        long requesterUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        var role = await ReadRoleAsync(connection, requesterUserId, cancellationToken);
        if (role != UserRole.Admin)
        {
            throw new UnauthorizedAccessException(
                "فقط کاربر مدیر می‌تواند آمار کارکرد همه کاربران را ببیند و مقایسه کند.");
        }

        var fromTehran = _clock.FormatTehranDate(fromUtc);
        var toTehran = _clock.FormatTehranDate(toUtc);
        var rows = await connection.QueryAsync<ComparisonRow>(
            new CommandDefinition(
                """
                SELECT u.id                                      AS UserId,
                       u.username                                AS Username,
                       u.display_name                            AS DisplayName,
                       u.role                                    AS Role,
                       COALESCE(SUM(s.pdf_import_count),  0)     AS TotalPdfImports,
                       COALESCE(SUM(s.note_create_count), 0)     AS TotalNotesCreated,
                       COALESCE(SUM(s.session_count),     0)     AS TotalSessions,
                       COALESCE(SUM(s.active_secs),       0)     AS TotalActiveSecs
                  FROM users u
                  LEFT JOIN user_daily_stats s
                         ON s.user_id = u.id
                        AND s.tehran_date BETWEEN @From AND @To
                 GROUP BY u.id, u.username, u.display_name, u.role
                 ORDER BY TotalPdfImports DESC, TotalNotesCreated DESC, u.username
                """,
                new { From = fromTehran, To = toTehran },
                cancellationToken: cancellationToken));

        return rows.Select(row => new UserComparisonRow(
            row.UserId,
            row.Username,
            row.DisplayName,
            UserRepository.ParseRole(row.Role),
            (int)row.TotalPdfImports,
            (int)row.TotalNotesCreated,
            (int)row.TotalSessions,
            (int)row.TotalActiveSecs)).ToList();
    }

    public async Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(
        long requesterUserId,
        long targetUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        using var connection = _factory.Create();
        await EnsureCanViewAsync(connection, requesterUserId, targetUserId, cancellationToken);

        var rows = await connection.QueryAsync<SessionRow>(
            new CommandDefinition(
                """
                SELECT id, login_utc, logout_utc, tehran_date, duration_secs
                  FROM user_sessions
                 WHERE user_id = @UserId
                   AND login_utc BETWEEN @From AND @To
                 ORDER BY login_utc DESC
                """,
                new { UserId = targetUserId, From = FormatUtc(fromUtc), To = FormatUtc(toUtc) },
                cancellationToken: cancellationToken));

        return rows.Select(row => new SessionSummary(
            row.id,
            ParseUtc(row.login_utc),
            row.logout_utc is null ? null : ParseUtc(row.logout_utc),
            row.tehran_date,
            row.duration_secs is null ? null : (int)row.duration_secs.Value)).ToList();
    }

    private static async Task EnsureCanViewAsync(
        System.Data.IDbConnection connection,
        long requesterUserId,
        long targetUserId,
        CancellationToken cancellationToken)
    {
        await EnsureUserExistsAsync(connection, targetUserId, cancellationToken);
        if (requesterUserId == targetUserId)
        {
            await EnsureUserExistsAsync(connection, requesterUserId, cancellationToken);
            return;
        }

        var role = await ReadRoleAsync(connection, requesterUserId, cancellationToken);
        if (role != UserRole.Admin)
        {
            throw new UnauthorizedAccessException("کاربر عادی فقط آمار کارکرد خودش را می‌تواند ببیند.");
        }
    }

    private static async Task EnsureUserExistsAsync(
        System.Data.IDbConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var exists = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM users WHERE id = @Id",
                new { Id = userId },
                cancellationToken: cancellationToken));
        if (exists == 0)
            throw new InvalidOperationException($"User {userId} does not exist.");
    }

    private static async Task<UserRole> ReadRoleAsync(
        System.Data.IDbConnection connection,
        long userId,
        CancellationToken cancellationToken)
    {
        var role = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT role FROM users WHERE id = @Id",
                new { Id = userId },
                cancellationToken: cancellationToken));

        if (role is null)
            throw new InvalidOperationException($"User {userId} does not exist.");

        return UserRepository.ParseRole(role);
    }

    private static async Task UpsertDailyStatAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        long userId,
        string tehranDate,
        int pdfDelta = 0,
        int noteDelta = 0,
        int sessionCountDelta = 0,
        int activeSecsDelta = 0,
        CancellationToken cancellationToken = default)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO user_daily_stats
                    (user_id, tehran_date, pdf_import_count, note_create_count, session_count, active_secs)
                VALUES (@UserId, @Date, @Pdf, @Note, @Sessions, @Secs)
                ON CONFLICT(user_id, tehran_date) DO UPDATE SET
                    pdf_import_count  = pdf_import_count  + excluded.pdf_import_count,
                    note_create_count = note_create_count + excluded.note_create_count,
                    session_count     = session_count     + excluded.session_count,
                    active_secs       = active_secs       + excluded.active_secs
                """,
                new
                {
                    UserId = userId,
                    Date = tehranDate,
                    Pdf = pdfDelta,
                    Note = noteDelta,
                    Sessions = sessionCountDelta,
                    Secs = activeSecsDelta
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static string FormatUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class OpenSessionRow
    {
        public long id { get; set; }
        public string login_utc { get; set; } = string.Empty;
    }

    private sealed class DailyActivityRow
    {
        public string TehranDate { get; set; } = string.Empty;
        public long PdfImports { get; set; }
        public long NotesCreated { get; set; }
        public long SessionCount { get; set; }
        public long ActiveSecs { get; set; }
    }

    private sealed class DailyRow
    {
        public long pdf_import_count { get; set; }
        public long note_create_count { get; set; }
        public long session_count { get; set; }
        public long active_secs { get; set; }
    }

    private sealed class ComparisonRow
    {
        public long UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = AppConstants.RoleUser;
        public long TotalPdfImports { get; set; }
        public long TotalNotesCreated { get; set; }
        public long TotalSessions { get; set; }
        public long TotalActiveSecs { get; set; }
    }

    private sealed class SessionRow
    {
        public long id { get; set; }
        public string login_utc { get; set; } = string.Empty;
        public string? logout_utc { get; set; }
        public string tehran_date { get; set; } = string.Empty;
        public long? duration_secs { get; set; }
    }
}
