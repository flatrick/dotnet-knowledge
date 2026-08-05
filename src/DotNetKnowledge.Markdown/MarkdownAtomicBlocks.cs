using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace DotNetKnowledge.Markdown;

public static class MarkdownAtomicBlocks
{
    public static IReadOnlyList<(int StartLine, int EndLine)> Find(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var blocks = new List<(int StartLine, int EndLine)>();

        foreach (var fenced in document.Descendants<FencedCodeBlock>())
        {
            var lastContentLine = fenced.Lines.Count > 0
                ? fenced.Lines.Lines[fenced.Lines.Count - 1].Line
                : fenced.Line;
            blocks.Add((fenced.Line + 1, lastContentLine + 3));
        }

        foreach (var table in document.Descendants<Table>())
        {
            var rows = table.OfType<TableRow>().ToArray();
            if (rows.Length == 0)
                continue;
            var lastRowLine = rows[^1].Line;
            var lastPhysicalLine = Math.Max(lastRowLine, table.Line + 1);
            blocks.Add((table.Line + 1, lastPhysicalLine + 2));
        }

        return blocks.OrderBy(block => block.StartLine).ToArray();
    }
}
