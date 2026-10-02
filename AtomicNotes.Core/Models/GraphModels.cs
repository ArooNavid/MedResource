namespace AtomicNotes.Core.Models;

/// <summary>
/// Mutable node used by the force-directed simulation.
/// </summary>
public sealed class GraphNode
{
    public int NoteId { get; set; }
    public string Title { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public double Fx { get; set; }
    public double Fy { get; set; }
    public int Degree { get; set; }
    public bool IsPinned { get; set; }
    public bool IsSelected { get; set; }
    public double Radius => Math.Clamp(10 + Degree * 2.5, 10, 32);
}

public sealed class GraphEdge
{
    public int SourceNoteId { get; set; }
    public int TargetNoteId { get; set; }
    public bool IsResolved { get; set; }
}
