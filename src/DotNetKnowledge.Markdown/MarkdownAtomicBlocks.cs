using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace DotNetKnowledge.Markdown;

public sealed record MarkdownBlockRange(int StartLine, int EndLine);

public static class MarkdownAtomicBlocks
{
    public static IReadOnlyList<MarkdownBlockRange> Find(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        // Parse the normalized string so Markdig's own Line numbers agree with the normalized
        // convention every other line-numbering consumer in this library uses (see MarkdownText).
        var normalized = MarkdownText.Normalize(markdown);
        var document = Markdig.Markdown.Parse(normalized, MarkdownPipelines.Default);
        var blocks = new List<MarkdownBlockRange>();

        foreach (var fenced in document.Descendants<FencedCodeBlock>())
        {
            var lastContentLine = fenced.Lines.Count > 0
                ? fenced.Lines.Lines[fenced.Lines.Count - 1].Line
                : fenced.Line;
            blocks.Add(new MarkdownBlockRange(fenced.Line + 1, lastContentLine + 3));
        }

        foreach (var table in document.Descendants<Table>())
        {
            var rows = table.OfType<TableRow>().ToArray();
            if (rows.Length == 0)
                continue;
            var lastRowLine = rows[^1].Line;
            var lastPhysicalLine = Math.Max(lastRowLine, table.Line + 1);
            blocks.Add(new MarkdownBlockRange(table.Line + 1, lastPhysicalLine + 2));
        }

        // Front matter is a single semantic unit like a fence or a table: a page boundary inside it
        // splits a key from its value. Same arithmetic as the fenced case — Line is the 0-based
        // opening "---", the last content line plus three is the exclusive end past the closing one.
        // An empty block ("---\n---") has no content lines, so it falls back to its own start.
        foreach (var frontMatter in document.Descendants<YamlFrontMatterBlock>())
        {
            var lastContentLine = frontMatter.Lines.Count > 0
                ? frontMatter.Lines.Lines[frontMatter.Lines.Count - 1].Line
                : frontMatter.Line;
            blocks.Add(new MarkdownBlockRange(frontMatter.Line + 1, lastContentLine + 3));
        }

        return blocks.OrderBy(block => block.StartLine).ToArray();
    }
}
