using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace DotNetKnowledge.Markdown;

/// <summary>
/// Locates where a document's content begins, so front matter can be excluded from both search and
/// fetch by one rule rather than two. Microsoft Learn articles carry a YAML block of
/// <c>title</c>/<c>ms.author</c>/<c>ms.date</c> keys that is metadata about the document, not part
/// of it.
/// </summary>
public static class MarkdownFrontMatter
{
    /// <summary>
    /// The 1-based line where <paramref name="markdown"/>'s content begins: the first non-blank
    /// line after a leading YAML front-matter block, or 1 when the document has none. A document
    /// that is nothing but front matter returns one past its last line, which makes a fetch range
    /// empty rather than out of bounds.
    /// </summary>
    public static int BodyStartLine(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = MarkdownText.Normalize(markdown);
        var document = Markdig.Markdown.Parse(normalized, MarkdownPipelines.Default);
        var frontMatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (frontMatter is null)
            return 1;

        // Same arithmetic as MarkdownAtomicBlocks: Line is the 0-based opening "---", and the last
        // content line plus three is the first line past the closing one.
        var lastContentLine = frontMatter.Lines.Count > 0
            ? frontMatter.Lines.Lines[frontMatter.Lines.Count - 1].Line
            : frontMatter.Line;

        var lines = MarkdownText.SplitLines(normalized);
        var line = lastContentLine + 3;

        // Skip the blank line Learn authors leave after the fence; otherwise every fetched article
        // would open with one.
        while (line <= lines.Length && string.IsNullOrWhiteSpace(lines[line - 1]))
            line++;

        return line;
    }
}
