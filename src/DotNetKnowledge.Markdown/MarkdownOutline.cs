using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DotNetKnowledge.Markdown;

public static class MarkdownOutline
{
    public static IReadOnlyList<MarkdownHeading> Extract(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        // Parse the normalized string, not the raw one: Markdig's own span offsets and this
        // method's totalLines count must agree on what counts as a line break, or StartLine and
        // EndLine silently disagree with everyone else who reads them (MarkdownLineSearch and
        // LanguageDocsQueryService both split lines via the same normalized convention).
        var normalized = MarkdownText.Normalize(markdown);
        var document = Markdig.Markdown.Parse(normalized, MarkdownPipelines.Default);
        var totalLines = MarkdownText.SplitLines(normalized).Length;

        // HeadingBlock.Line is not usable directly: for a setext heading ("Title\n-----") it
        // points at the underline row, not the heading text itself, because the block parser only
        // confirms the heading once it sees the underline. The character span's start offset maps
        // to the correct line for both ATX and setext forms.
        var raw = document.Descendants<HeadingBlock>()
            .Select(heading => (Level: heading.Level, Text: RenderPlainText(heading.Inline), StartLine: LineNumberAt(normalized, heading.Span.Start)))
            .ToArray();

        var headings = new List<MarkdownHeading>(raw.Length);
        var ancestorStack = new List<(int Level, string Text)>();
        var pathOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < raw.Length; i++)
        {
            var (level, text, startLine) = raw[i];

            while (ancestorStack.Count > 0 && ancestorStack[^1].Level >= level)
                ancestorStack.RemoveAt(ancestorStack.Count - 1);

            var basePath = string.Join(" > ", ancestorStack.Select(ancestor => ancestor.Text).Append(text));
            var occurrence = pathOccurrences.TryGetValue(basePath, out var count) ? count + 1 : 1;
            pathOccurrences[basePath] = occurrence;
            var path = occurrence == 1 ? basePath : $"{basePath} ({occurrence})";

            ancestorStack.Add((level, text));

            var endLine = totalLines + 1;
            for (var j = i + 1; j < raw.Length; j++)
            {
                if (raw[j].Level <= level)
                {
                    endLine = raw[j].StartLine;
                    break;
                }
            }

            headings.Add(new MarkdownHeading(level, text, path, startLine, endLine));
        }

        return headings;
    }

    private static int LineNumberAt(string markdown, int charOffset)
    {
        var line = 1;
        for (var i = 0; i < charOffset; i++)
        {
            if (markdown[i] == '\n')
                line++;
        }

        return line;
    }

    private static string RenderPlainText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;

        var builder = new System.Text.StringBuilder();

        void Walk(Inline? node)
        {
            while (node is not null)
            {
                switch (node)
                {
                    case LiteralInline literal:
                        builder.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        builder.Append(code.Content);
                        break;
                    case ContainerInline container:
                        Walk(container.FirstChild);
                        break;
                }

                node = node.NextSibling;
            }
        }

        Walk(inline);
        return builder.ToString();
    }
}
