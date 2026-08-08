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
        var compiled = regex ? new Regex(pattern, RegexOptions.NonBacktracking) : null;
        return Search(markdown, outline, pattern, compiled);
    }

    /// <summary>
    /// Same as the <c>bool regex</c> overload, but accepts an already-built <see cref="Regex"/> so a
    /// caller searching many documents with the same pattern (e.g. a source-wide file scan) builds
    /// it once instead of once per document.
    /// </summary>
    public static IReadOnlyList<MarkdownLineHit> Search(
        string markdown,
        IReadOnlyList<MarkdownHeading> outline,
        string pattern,
        Regex? compiledPattern)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var lines = MarkdownText.SplitLines(MarkdownText.Normalize(markdown));
        var hits = new List<MarkdownLineHit>();

        // Front matter is metadata, not part of the document. Matching it would produce hits with
        // no enclosing section, at lines get_doc does not return — a location the caller cannot
        // follow. This costs one extra parse for a file that reached this far, which the caller's
        // own prefilter has already narrowed to files that can match.
        var bodyStartLine = MarkdownFrontMatter.BodyStartLine(markdown);

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            if (lineNumber < bodyStartLine)
                continue;

            var matched = compiledPattern is not null
                ? compiledPattern.IsMatch(lines[i])
                : lines[i].Contains(pattern, StringComparison.Ordinal);
            if (!matched)
                continue;

            var section = outline.LastOrDefault(
                heading => heading.StartLine <= lineNumber && lineNumber < heading.EndLine);
            hits.Add(new MarkdownLineHit(lineNumber, lines[i], section?.Path ?? string.Empty));
        }

        return hits;
    }
}
