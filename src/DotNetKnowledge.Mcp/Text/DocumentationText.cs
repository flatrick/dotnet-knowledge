using System.Text;
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Mcp.Text;

/// <summary>
/// Normalizes documentation text on the way out of a source, and applies the size budget on the way
/// into a payload.
/// </summary>
/// <remarks>
/// These are two stages, not one, and the order is load-bearing. Normalization runs where text is
/// read, so that search matches the same string a caller is shown — <c>search_api_text</c> can only
/// find a phrase spanning a resolved reference because matching happens after normalization.
/// Budgeting runs at the payload, after matching and paging, because capping text before a match
/// would silently drop every hit past the cap.
/// </remarks>
public static partial class DocumentationText
{
    /// <summary>
    /// MSDocs markdown references: <c>&lt;xref:System.String&gt;</c>. The target may carry generic
    /// arity, a trailing <c>*</c> naming an overload group, a <c>?displayProperty=</c> query, and
    /// URL-encoded punctuation — all presentation, none of it part of the symbol.
    /// </summary>
    [GeneratedRegex(@"<xref:([^>]+)>")]
    private static partial Regex XrefPattern { get; }

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex ParagraphBreakPattern { get; }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern { get; }

    /// <summary>
    /// Resolves MSDocs references and, when <paramref name="collapseWhitespace"/> is set, folds the
    /// source's own line wrapping and indentation away. Returns null for absent or placeholder text.
    /// </summary>
    /// <param name="collapseWhitespace">
    /// False for markdown-bodied documentation. Collapsing there would run fenced code blocks and
    /// list items into a single line, destroying the structure that makes them readable; the
    /// indentation this removes is an artifact only of XML that wraps its prose.
    /// </param>
    public static string? Normalize(string? text, bool collapseWhitespace)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var resolved = ResolveXrefs(text);
        if (collapseWhitespace)
            resolved = CollapseWhitespace(resolved);

        resolved = resolved.Trim();

        // ECMA XML's placeholder for undocumented members. It is not an answer, and returning it
        // would read as one.
        return resolved.Length == 0 || string.Equals(resolved, "To be added.", StringComparison.Ordinal)
            ? null
            : resolved;
    }

    /// <summary>
    /// Caps text to a character budget, reporting whether anything was removed rather than marking
    /// it in the text. A structural flag is what a caller can act on; a trailing ellipsis has to be
    /// parsed back out, and is indistinguishable from one the source itself wrote.
    /// </summary>
    public static (string Text, bool IsTruncated) Budget(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        return text.Length <= maxLength ? (text, false) : (text[..maxLength], true);
    }

    private static string ResolveXrefs(string text)
    {
        if (!text.Contains("<xref:", StringComparison.Ordinal))
            return text;

        return XrefPattern.Replace(text, match =>
        {
            var target = match.Groups[1].ValueSpan;

            // Everything from '?' is a display directive for the docs site.
            var query = target.IndexOf('?');
            if (query >= 0)
                target = target[..query];

            // A trailing '*' names every overload rather than one signature.
            target = target.TrimEnd('*');

            return Uri.UnescapeDataString(target.ToString());
        });
    }

    private static string CollapseWhitespace(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var paragraphs = ParagraphBreakPattern.Split(normalized);
        var builder = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var folded = WhitespaceRunPattern.Replace(paragraph, " ").Trim();
            if (folded.Length == 0)
                continue;

            if (builder.Length > 0)
                builder.Append("\n\n");
            builder.Append(folded);
        }

        return builder.ToString();
    }
}
