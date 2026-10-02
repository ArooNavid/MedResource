namespace AtomicNotes.Core.Models;

public sealed record VaultImportResult(int Imported, int Skipped, IReadOnlyList<string> Messages);
