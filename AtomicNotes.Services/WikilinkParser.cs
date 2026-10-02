using System.Text.RegularExpressions;

namespace AtomicNotes.Services;

public static partial class WikilinkParser
{
    [GeneratedRegex(@"\[\[([^\[\]\|]+)(?:\|[^\[\]]+)?\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    public static IReadOnlyList<string> ExtractTargets(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return Array.Empty<string>();

        return LinkPattern()
            .Matches(markdown)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
