using System.Net;
using System.Text.RegularExpressions;
using AtomicNotes.Core.Interfaces;
using AtomicNotes.Core.Models;
using Markdig;

namespace AtomicNotes.Services;

/// <summary>
/// Stage 26: HTML preview for note markdown, including task lists and wikilinks.
/// </summary>
public sealed class MarkdownPreviewService : IMarkdownPreviewService
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly Regex Wikilink = new(@"\[\[([^\[\]\|]+)(?:\|([^\[\]]+))?\]\]", RegexOptions.CultureInvariant);

    private readonly INoteLinkService _links;

    public MarkdownPreviewService(INoteLinkService links) => _links = links;

    public async Task<string> RenderAsync(string markdown, long? noteId = null, CancellationToken ct = default)
    {
        var prepared = noteId is null
            ? ReplaceWikilinksWithoutNote(markdown ?? string.Empty)
            : await ReplaceWikilinksForNoteAsync(noteId.Value, markdown ?? string.Empty, ct);
        return Markdown.ToHtml(prepared, Pipeline);
    }

    private async Task<string> ReplaceWikilinksForNoteAsync(long noteId, string markdown, CancellationToken ct)
    {
        var outgoing = await _links.GetOutgoingLinksAsync((int)noteId, ct);
        var byRaw = outgoing.ToDictionary(link => link.RawTarget, link => link, StringComparer.OrdinalIgnoreCase);
        return Wikilink.Replace(markdown, match =>
        {
            var target = match.Groups[1].Value.Trim();
            var alias = match.Groups[2].Success ? match.Groups[2].Value.Trim() : target;
            if (!byRaw.TryGetValue(target, out var link) || link.TargetNoteId is null)
                return $"[{alias}](#dangling-{Encode(target)})";
            return $"[{alias}](#note-{link.TargetNoteId.Value})";
        });
    }

    private static string ReplaceWikilinksWithoutNote(string markdown) =>
        Wikilink.Replace(markdown, match =>
        {
            var target = match.Groups[1].Value.Trim();
            var alias = match.Groups[2].Success ? match.Groups[2].Value.Trim() : target;
            return $"[{alias}](#dangling-{Encode(target)})";
        });

    private static string Encode(string value) => WebUtility.UrlEncode(value);
}
