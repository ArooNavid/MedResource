namespace AtomicNotes.Core;

/// <summary>
/// Single source of truth for application-wide constants.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Maximum folder/note nesting depth, 1-based (root = level 1).
    /// Stage 1 used 10; Stage 8 used 15. Final decision is 15.
    /// </summary>
    public const int MaxTreeDepth = 15;

    public const string DbFileName = "atomicnotes.db";

    /// <summary>
    /// Highest applied migration:
    /// 1 baseline, 2 users/sessions, 3 role column, 4 per-user activity stats.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    public const string FrontmatterDateFormat = "yyyy-MM-dd";
    public const string MarkdownExtension = ".md";
    public const string TehranDateFormat = "yyyy-MM-dd";

    public const int ImportBatchSize = 50;

    /// <summary>Canonical import status values. The final state is Committed, never Completed.</summary>
    public const string ImportStatusPrepared = "Prepared";
    public const string ImportStatusStaged = "Staged";
    public const string ImportStatusCommitted = "Committed";
    public const string ImportStatusFailed = "Failed";

    public const string RoleUser = "User";
    public const string RoleAdmin = "Admin";
}
