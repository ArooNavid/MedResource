namespace AtomicNotes.Core;

/// <summary>
/// Single source of truth for decisions that the stage files disagreed on.
/// </summary>
public static class AppConstants
{
    /// <summary>1-based nesting limit. Stage 1 said 10; the locked decision is 15.</summary>
    public const int MaxTreeDepth = 15;

    public const string DbFileName = "atomicnotes.db";

    /// <summary>
    /// 1 baseline, 2 users, 3 role, 4 per-user activity,
    /// 5 FTS, 6 tags, 7 wikilinks, 8 Obsidian sync state.
    /// </summary>
    public const int CurrentSchemaVersion = 8;

    public const string MarkdownExtension = ".md";
    public const string TehranDateFormat = "yyyy-MM-dd";

    public const string ImportStatusPrepared = "Prepared";
    public const string ImportStatusStaged = "Staged";
    public const string ImportStatusCommitted = "Committed";
    public const string ImportStatusFailed = "Failed";

    public const string RoleUser = "User";
    public const string RoleAdmin = "Admin";

    public const int MinBackupIntervalHours = 1;
    public const int MaxBackupIntervalHours = 168;

    /// <summary>Stage 22. One markdown file per Tehran day lives under this vault folder.</summary>
    public const string DailyNotesFolder = "daily";

    public const string DailyNoteTag = "روزانه";
}
