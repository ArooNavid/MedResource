namespace AtomicNotes.Core.Models;

/// <summary>Stage 45: aggregate counts for the markdown vault and link graph.</summary>
public sealed record VaultStats(
    int ActiveNotes,
    int TrashedNotes,
    int PinnedNotes,
    int TotalLinks,
    int UnresolvedLinks,
    int Tags,
    int Aliases,
    int Templates,
    int MarkdownFilesOnDisk,
    long TotalWords);
