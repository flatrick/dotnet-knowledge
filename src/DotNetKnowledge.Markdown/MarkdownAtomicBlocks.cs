using Markdig;
using Markdig.Extensions.Tables;
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
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var document = Markdig.Markdown.Parse(normalized, pipeline);
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

        return blocks.OrderBy(block => block.StartLine).ToArray();
    }
}
