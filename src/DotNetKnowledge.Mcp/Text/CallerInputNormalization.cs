using System.Net;
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Mcp.Text;

/// <summary>
/// Fixes reversible encoding artifacts in caller-supplied text — HTML entities and common
/// typographic substitutions — before a second match attempt, never the first.
/// </summary>
/// <remarks>
/// See docs/superpowers/specs/2026-08-09-caller-input-normalization-design.md. This type has no
/// opinion about when it is safe to call; every call site decides that for itself by only invoking
/// it from inside a failure path the literal input has already taken.
/// </remarks>
public static partial class CallerInputNormalization
{
    [GeneratedRegex("[\u2018\u2019]")]
    private static partial Regex SingleCurlyQuotePattern { get; }

    [GeneratedRegex("[\u201C\u201D]")]
    private static partial Regex DoubleCurlyQuotePattern { get; }

    /// <summary>
    /// Decodes HTML entities, folds curly quotes to straight ones, and folds a non-breaking space
    /// to a regular one. Returns whether <paramref name="normalized"/> actually differs from
    /// <paramref name="input"/>, so a caller only pays for a second match attempt when this could
    /// plausibly change the outcome.
    /// </summary>
    public static bool TryNormalize(string input, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(input);

        var decoded = WebUtility.HtmlDecode(input);
        var straightened = SingleCurlyQuotePattern.Replace(decoded, "'");
        straightened = DoubleCurlyQuotePattern.Replace(straightened, "\"");
        normalized = straightened.Replace('\u00A0', ' ');

        return !string.Equals(normalized, input, StringComparison.Ordinal);
    }
}
