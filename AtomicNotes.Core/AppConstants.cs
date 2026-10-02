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
    /// 5 FTS, 6 tags, 7 wikilinks, 8 Obsidian sync state, 9 pinned notes.
    /// </summary>
    public const int CurrentSchemaVersion = 9;

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

    /// <summary>Stage 23. Markdown templates stay in this vault folder and are not notes.</summary>
    public const string TemplatesFolder = "templates";

    /// <summary>File name, without extension, whose body fills a new daily note.</summary>
    public const string DailyTemplateName = "daily";

    public static bool IsIgnoredVaultRelativePath(string? relativePath)
    {
        var rel = (relativePath ?? "").Replace('\\', '/').Trim().TrimStart('/');
        return rel.Equals(".staging", StringComparison.OrdinalIgnoreCase)
               || rel.StartsWith(".staging/", StringComparison.OrdinalIgnoreCase)
               || rel.Contains("/.staging/", StringComparison.OrdinalIgnoreCase)
               || rel.Equals(TemplatesFolder, StringComparison.OrdinalIgnoreCase)
               || rel.StartsWith(TemplatesFolder + "/", StringComparison.OrdinalIgnoreCase);
    }
}
