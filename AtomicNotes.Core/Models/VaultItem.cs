namespace AtomicNotes.Core.Models;

public sealed class VaultItem
{
    public long Id { get; set; }
    public string VaultPath { get; set; } = string.Empty;
    public string RelPath { get; set; } = string.Empty;
    public string ItemType { get; set; } = "note";
    public string Title { get; set; } = string.Empty;
    public int Depth { get; set; }
    public long? ParentId { get; set; }
}
