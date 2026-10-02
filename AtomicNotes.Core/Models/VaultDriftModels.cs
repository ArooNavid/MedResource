namespace AtomicNotes.Core.Models;

public sealed record VaultDriftEntry(string RelPath, long? NoteId, string? Title);

/// <summary>Stage 48: mismatch between vault markdown files and note rows.</summary>
public sealed record VaultDriftReport(
    IReadOnlyList<VaultDriftEntry> OnlyOnDisk,
    IReadOnlyList<VaultDriftEntry> MissingFileOnDisk);

public sealed record VaultDriftImportResult(int Imported, IReadOnlyList<string> Messages);
