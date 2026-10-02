namespace AtomicNotes.Core.Models;

public enum VaultImportAction
{
    Skipped,
    New,
    Updated,
    Invalid
}

public sealed record VaultImportEntry(string RelPath, VaultImportAction Action, string Message);

public sealed record VaultImportResult(
    int Imported,
    int Updated,
    int Skipped,
    bool Preview,
    IReadOnlyList<VaultImportEntry> Entries);
