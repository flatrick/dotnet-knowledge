namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

/// <summary>
/// Orders <see cref="LanguageDocLineHit"/> matches by how authoritative the containing document is,
/// then by whether the match landed on a heading. A query like "collection expressions" otherwise
/// drowns under LDM meeting-note agenda lines that merely name the feature, while the proposal that
/// defines it sits pages down. Document weight comes from the repo-relative path; the ordering ends
/// in the same path/line/repo ordinal tiebreak the service used before, so paging stays
/// deterministic.
/// </summary>
public static class LanguageDocRanking
{
    /// <summary>
    /// Returns <paramref name="hits"/> ordered most-authoritative first for <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="query"/> is accepted for symmetry with the other rankers and to leave room for
    /// query-dependent weighting; today the ordering is driven by the hit's path and text alone.
    /// </remarks>
    public static IReadOnlyList<LanguageDocLineHit> Order(IEnumerable<LanguageDocLineHit> hits, string query)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(query);

        return hits
            .OrderBy(hit => DocumentTypeRank(hit.Path))
            .ThenBy(hit => HeadingRank(hit.Text))
            .ThenBy(hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(hit => hit.Line)
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();
    }

    // A proposal or the specification defines a feature; a meeting note discusses it in passing. The
    // slash-terminated segments keep "proposal-a.md" (a filename) from reading as the proposals tree.
    private static int DocumentTypeRank(string path)
    {
        if (path.Contains("proposals/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("spec/", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (path.Contains("meetings/", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    // A match on a heading names the section the reader wants; a match in prose is one mention inside
    // it. ATX headings are the leading '#' run markdown uses.
    private static int HeadingRank(string text) =>
        text.TrimStart().StartsWith('#') ? 0 : 1;
}
