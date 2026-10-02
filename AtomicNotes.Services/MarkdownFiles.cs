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

    public static string Compose(string title, int depth, IEnumerable<string> tags, string body, string? sourcePdf = null)
    {
        var front = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["depth"] = depth,
            ["tags"] = tags.ToList(),
            ["updated"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
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

    public static (string Title, int Depth, List<string> Tags, string Body) Parse(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
            return (string.Empty, 1, new List<string>(), markdown);

        var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            return (string.Empty, 1, new List<string>(), markdown);

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
            var tags = new List<string>();
            if (map.TryGetValue("tags", out var rawTags) && rawTags is IEnumerable<object> list)
                tags.AddRange(list.Select(item => item?.ToString() ?? "").Where(item => item.Length > 0));
            return (title, depth, tags, body);
        }
        catch
        {
            return (string.Empty, 1, new List<string>(), body);
        }
    }

    public static string SanitizeFileName(string title)
    {
        var cleaned = string.Concat((title ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        if (cleaned.Length == 0)
            cleaned = "note";
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
