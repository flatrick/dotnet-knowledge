namespace DotNetKnowledge.Markdown;

/// <summary>
/// The one place in this library that defines what counts as a line break, so every consumer of a
/// <see cref="MarkdownHeading"/>'s <c>StartLine</c>/<c>EndLine</c> agrees on the same line numbers.
/// <see cref="string.ReplaceLineEndings(string)"/> folds <c>\r\n</c>, a bare <c>\r</c>, and several
/// Unicode line separators (NEL, LS, PS, form feed) into <c>\n</c>; naive <c>\n</c>-counting against
/// raw text does not, and the two disagreeing is what this type exists to prevent.
/// </summary>
internal static class MarkdownText
{
    public static string Normalize(string markdown) => markdown.ReplaceLineEndings("\n");

    public static string[] SplitLines(string normalizedMarkdown) => normalizedMarkdown.Split('\n');
}
