namespace AtomicNotes.Core.Models;

public enum DailyCountType
{
    PdfImport,
    NoteCreate
}

public sealed record SessionSummary(
    long SessionId,
    DateTime LoginUtc,
    DateTime? LogoutUtc,
    string TehranDate,
    int? DurationSecs);

public sealed record DailyActivity(
    string TehranDate,
    int PdfImports,
    int NotesCreated,
    int SessionCount,
    int ActiveSecs);

public sealed record UserDailyStats(
    long UserId,
    string TehranDate,
    int PdfImportCount,
    int NoteCreateCount,
    int SessionCount,
    int ActiveSecs);

public sealed record UserActivityReport(
    long UserId,
    string Username,
    DateTime RangeStartUtc,
    DateTime RangeEndUtc,
    int TotalPdfImports,
    int TotalNotesCreated,
    int TotalSessions,
    int TotalActiveSecs,
    IReadOnlyList<DailyActivity> DailyBreakdown);

public sealed record UserComparisonRow(
    long UserId,
    string Username,
    string DisplayName,
    UserRole Role,
    int TotalPdfImports,
    int TotalNotesCreated,
    int TotalSessions,
    int TotalActiveSecs);
