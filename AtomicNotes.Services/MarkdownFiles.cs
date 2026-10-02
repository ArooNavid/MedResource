using System.Text;
using AtomicNotes.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AtomicNotes.Services;

public static class MarkdownFiles
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string Compose(
        string title,
        int depth,
        IEnumerable<string> tags,
        string body,
        string? sourcePdf = null,
        string? created = null,
        string? updated = null,
        IEnumerable<string>? aliases = null)
    {
        var aliasList = (aliases ?? Array.Empty<string>())
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var front = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["depth"] = depth,
            ["tags"] = tags.ToList(),
            ["created"] = created ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["updated"] = updated ?? DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
        if (aliasList.Count > 0)
            front["aliases"] = aliasList;
        if (!string.IsNullOrWhiteSpace(sourcePdf))
            front["sourcePdf"] = sourcePdf;

        var yaml = Serializer.Serialize(front);
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append(yaml);
        if (!yaml.EndsWith('\n'))
            builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.Append(body?.Replace("\r\n", "\n") ?? string.Empty);
        return builder.ToString();
    }

    public static MarkdownDocument Parse(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
            return new MarkdownDocument(string.Empty, 1, Array.Empty<string>(), Array.Empty<string>(), markdown, null, null);

        var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            return new MarkdownDocument(string.Empty, 1, Array.Empty<string>(), Array.Empty<string>(), markdown, null, null);

        var yaml = markdown[4..end];
        var bodyStart = end + 4;
        while (bodyStart < markdown.Length && (markdown[bodyStart] == '\n' || markdown[bodyStart] == '\r'))
            bodyStart++;
        var body = markdown[bodyStart..];

        try
        {
            var map = Deserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new();
            var title = map.TryGetValue("title", out var rawTitle) ? rawTitle?.ToString() ?? "" : "";
            var depth = 1;
            if (map.TryGetValue("depth", out var rawDepth) && int.TryParse(rawDepth?.ToString(), out var parsed))
                depth = Math.Clamp(parsed, 1, AppConstants.MaxTreeDepth);
            var tags = ReadTags(map);
            var aliases = ReadTags(map, "aliases");
            var created = map.TryGetValue("created", out var rawCreated) ? rawCreated?.ToString() : null;
            var updated = map.TryGetValue("updated", out var rawUpdated) ? rawUpdated?.ToString() : null;
            return new MarkdownDocument(title, depth, tags, aliases, body, created, updated);
        }
        catch
        {
            return new MarkdownDocument(string.Empty, 1, Array.Empty<string>(), Array.Empty<string>(), body, null, null);
        }
    }

    private static List<string> ReadTags(Dictionary<string, object> map, string key = "tags")
    {
        if (!map.TryGetValue(key, out var rawTags) || rawTags is null)
            return new List<string>();
        if (rawTags is string single)
            return single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (rawTags is IEnumerable<object> list)
            return list.Select(item => item?.ToString() ?? "").Where(item => item.Length > 0).ToList();
        return new List<string>();
    }

    public sealed record MarkdownDocument(
        string Title,
        int Depth,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Aliases,
        string Body,
        string? Created,
        string? Updated);

    public static string SanitizeFileName(string title)
    {
        var cleaned = string.Concat((title ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        if (cleaned.Length == 0)
            cleaned = "note";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
