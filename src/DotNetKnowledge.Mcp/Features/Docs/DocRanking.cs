namespace DotNetKnowledge.Mcp.Features.Docs;

/// <summary>
/// Orders <see cref="DocLineHit"/> matches by how authoritative the containing document is,
/// then by whether the match landed on a heading. A query like "collection expressions" otherwise
/// drowns under LDM meeting-note agenda lines that merely name the feature, while the proposal that
/// defines it sits pages down. Document weight comes from the repo-relative path, in four tiers:
/// proposals and the specification lead, current NuGet guidance is next, everything else
/// unclassified follows, and historical material (meeting notes, release notes, the archive) sits
/// last. The ordering ends in the same path/line/repo ordinal tiebreak the service used before, so
/// paging stays deterministic.
/// </summary>
public static class DocRanking
{
    /// <summary>
    /// Returns <paramref name="hits"/> ordered most-authoritative first for <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="query"/> is accepted for symmetry with the other rankers and to leave room for
    /// query-dependent weighting; today the ordering is driven by the hit's path and text alone.
    /// </remarks>
    public static IReadOnlyList<DocLineHit> Order(IEnumerable<DocLineHit> hits, string query)
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

    // Current NuGet guidance. The "docs/" prefix is load-bearing: it keeps these from colliding
    // with a future source's own "reference/" or "concepts/" tree. roslyn-wiki is the only other
    // source rooted at "docs/", and it holds "docs/wiki/" alone.
    private static readonly string[] NuGetGuidancePaths =
    [
        "docs/api/",
        "docs/concepts/",
        "docs/consume-packages/",
        "docs/create-packages/",
        "docs/guides/",
        "docs/hosting-packages/",
        "docs/nuget-org/",
        "docs/policies/",
        "docs/quickstart/",
        "docs/reference/",
        "docs/visual-studio-extensibility/",
    ];

    // Documents about the past. A meeting note discusses a feature in passing; a release note
    // describes a version long shipped. Of 1170 NuGet lines matching "restore", 479 are here.
    private static readonly string[] HistoricalPaths =
    [
        "meetings/",
        "docs/release-notes/",
        "docs/archive/",
    ];

    // A proposal or the specification defines a feature, so it leads. NuGet guidance sits below it
    // rather than beside it: equal ranks fall through to the path tiebreak, where "docs/" sorts
    // ahead of "proposals/", and a language query must not be answered by a packaging document.
    // The slash-terminated segments keep "proposal-a.md" (a filename) from reading as the
    // proposals tree.
    private static int DocumentTypeRank(string path)
    {
        if (path.Contains("proposals/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("spec/", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (HistoricalPaths.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            return 3;
        if (NuGetGuidancePaths.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            return 1;
        return 2;
    }

    // A match on a heading names the section the reader wants; a match in prose is one mention inside
    // it. ATX headings are the leading '#' run markdown uses.
    private static int HeadingRank(string text) =>
        text.TrimStart().StartsWith('#') ? 0 : 1;
}
