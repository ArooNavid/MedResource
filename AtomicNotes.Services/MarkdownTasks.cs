using System.Text.RegularExpressions;

namespace AtomicNotes.Services;

internal static partial class MarkdownTasks
{
    [GeneratedRegex(@"^\s*-\s*\[([ xX])\]\s+(.*)$")]
    private static partial Regex CheckboxLine();

    public static IReadOnlyList<(int LineIndex, string Text, bool IsDone)> Parse(string content)
    {
        var lines = (content ?? "").Replace("\r\n", "\n").Split('\n');
        var items = new List<(int, string, bool)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var match = CheckboxLine().Match(lines[index]);
            if (!match.Success)
                continue;
            items.Add((index, match.Groups[2].Value.Trim(), match.Groups[1].Value != " "));
        }
        return items;
    }

    public static string ToggleLine(string content, int lineIndex, bool done)
    {
        var lines = (content ?? "").Replace("\r\n", "\n").Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length)
            throw new InvalidOperationException("خط کار پیدا نشد.");

        var match = CheckboxLine().Match(lines[lineIndex]);
        if (!match.Success)
            throw new InvalidOperationException("این خط چک‌لیست نیست.");

        var mark = done ? "x" : " ";
        var indent = lines[lineIndex][..match.Index];
        lines[lineIndex] = $"{indent}- [{mark}] {match.Groups[2].Value}";
        return string.Join('\n', lines);
    }
}
