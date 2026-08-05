namespace DotNetKnowledge.Markdown;

public static class MarkdownPager
{
    public static (int EndLineExclusive, bool IsPartial) Page(
        IReadOnlyList<string> lines,
        IReadOnlyList<(int StartLine, int EndLine)> atomicBlocks,
        int startLine,
        int endLineExclusiveBound,
        int charBudget)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(atomicBlocks);

        var stopLine = startLine;
        var chars = 0;

        while (stopLine < endLineExclusiveBound)
        {
            var lineLength = lines[stopLine - 1].Length + 1;
            if (chars > 0 && chars + lineLength > charBudget)
                break;

            chars += lineLength;
            stopLine++;
        }

        // Never end in the middle of a fenced code block or a table: extend past any atomic
        // block that started before stopLine but has not yet ended, unless doing so would cross
        // the requested bound (a malformed document's unclosed fence must not pull the page past
        // where the caller asked it to stop).
        bool extended;
        do
        {
            extended = false;
            foreach (var block in atomicBlocks)
            {
                if (block.StartLine < stopLine && block.EndLine > stopLine && block.EndLine <= endLineExclusiveBound)
                {
                    stopLine = block.EndLine;
                    extended = true;
                }
            }
        } while (extended);

        return (stopLine, stopLine < endLineExclusiveBound);
    }
}
