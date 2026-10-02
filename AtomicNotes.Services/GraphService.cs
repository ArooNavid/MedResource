using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Dapper;

namespace AtomicNotes.Services;

public sealed class GraphService : IGraphService
{
    private readonly IDbConnectionFactory _factory;

    public GraphService(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)> LoadGraphAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        var notes = (await connection.QueryAsync<(long Id, string Title)>(
            new CommandDefinition("SELECT id AS Id, title AS Title FROM notes ORDER BY id", cancellationToken: ct))).ToList();

        var edges = (await connection.QueryAsync<GraphEdge>(
            new CommandDefinition(
                """
                SELECT source_note_id AS SourceNoteId,
                       target_note_id AS TargetNoteId
                  FROM note_links
                 WHERE target_note_id IS NOT NULL
                """,
                cancellationToken: ct))).ToList();

        foreach (var edge in edges)
            edge.IsResolved = true;

        var nodes = notes.Select(note => new GraphNode
        {
            NoteId = (int)note.Id,
            Title = note.Title
        }).ToList();

        var degree = nodes.ToDictionary(node => node.NoteId, _ => 0);
        foreach (var edge in edges)
        {
            if (degree.ContainsKey(edge.SourceNoteId))
                degree[edge.SourceNoteId]++;
            if (degree.ContainsKey(edge.TargetNoteId))
                degree[edge.TargetNoteId]++;
        }

        var count = Math.Max(nodes.Count, 1);
        var radius = Math.Max(200, nodes.Count * 12);
        for (var i = 0; i < nodes.Count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            nodes[i].Degree = degree[nodes[i].NoteId];
            nodes[i].X = radius * Math.Cos(angle);
            nodes[i].Y = radius * Math.Sin(angle);
        }

        return (nodes, edges);
    }
}
