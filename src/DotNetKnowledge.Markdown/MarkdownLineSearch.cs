using System.Text.RegularExpressions;

namespace DotNetKnowledge.Markdown;

public sealed record MarkdownLineHit(int Line, string Text, string SectionPath);

public static class MarkdownLineSearch
{
    public static IReadOnlyList<MarkdownLineHit> Search(
        string markdown,
        IReadOnlyList<MarkdownHeading> outline,
        string pattern,
        bool regex)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var compiled = regex ? new Regex(pattern, RegexOptions.NonBacktracking) : null;
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var hits = new List<MarkdownLineHit>();

        for (var i = 0; i < lines.Length; i++)
        {
            var matched = compiled is not null
                ? compiled.IsMatch(lines[i])
                : lines[i].Contains(pattern, StringComparison.Ordinal);
            if (!matched)
                continue;

            var lineNumber = i + 1;
            var section = outline.LastOrDefault(
                heading => heading.StartLine <= lineNumber && lineNumber < heading.EndLine);
            hits.Add(new MarkdownLineHit(lineNumber, lines[i], section?.Path ?? string.Empty));
        }

        return hits;
    }
}
