using AtomicNotes.Core.Models;

namespace AtomicNotes.Services;

/// <summary>
/// Pure physics. One call to <see cref="Step"/> advances the simulation by one frame.
/// </summary>
public sealed class ForceDirectedLayout
{
    public const double RepulsionK = 9000;
    public const double SpringK = 0.06;
    public const double RestLength = 160;
    public const double Damping = 0.82;
    public const double MaxV = 24;
    public const double Dt = 0.016;
    public const double Epsilon = 0.001;

    public double Step(IList<GraphNode> nodes, IList<GraphEdge> edges)
    {
        foreach (var node in nodes)
        {
            node.Fx = 0;
            node.Fy = 0;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var a = nodes[i];
                var b = nodes[j];
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq < Epsilon)
                {
                    dx = 0.01;
                    dy = 0.01;
                    distSq = dx * dx + dy * dy;
                }

                var dist = Math.Sqrt(distSq);
                var force = RepulsionK / distSq;
                var fx = force * dx / dist;
                var fy = force * dy / dist;
                a.Fx += fx;
                a.Fy += fy;
                b.Fx -= fx;
                b.Fy -= fy;
            }
        }

        var byId = nodes.ToDictionary(node => node.NoteId);
        foreach (var edge in edges)
        {
            if (!byId.TryGetValue(edge.SourceNoteId, out var source) ||
                !byId.TryGetValue(edge.TargetNoteId, out var target))
                continue;

            var dx = target.X - source.X;
            var dy = target.Y - source.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < Epsilon)
                dist = Epsilon;

            var displacement = dist - RestLength;
            var force = SpringK * displacement;
            var fx = force * dx / dist;
            var fy = force * dy / dist;
            source.Fx += fx;
            source.Fy += fy;
            target.Fx -= fx;
            target.Fy -= fy;
        }

        double energy = 0;
        foreach (var node in nodes)
        {
            if (node.IsPinned)
                continue;

            node.Vx = (node.Vx + node.Fx * Dt) * Damping;
            node.Vy = (node.Vy + node.Fy * Dt) * Damping;
            energy += node.Vx * node.Vx + node.Vy * node.Vy;

            var speed = Math.Sqrt(node.Vx * node.Vx + node.Vy * node.Vy);
            if (speed > MaxV)
            {
                node.Vx = node.Vx / speed * MaxV;
                node.Vy = node.Vy / speed * MaxV;
            }

            node.X += node.Vx * Dt;
            node.Y += node.Vy * Dt;
        }

        return energy;
    }
}
