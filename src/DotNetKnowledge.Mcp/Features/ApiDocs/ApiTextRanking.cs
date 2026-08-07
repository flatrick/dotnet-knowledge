namespace DotNetKnowledge.Mcp.Features.ApiDocs;

/// <summary>
/// Orders <see cref="ApiTextHit"/> prose matches by how much a reader learns from them rather than by
/// symbol name. A summary answers "what is this" in one line; a remark or an exception clause is
/// detail a caller reaches for only after. And a query that stands as its own word is a stronger
/// signal than the same letters buried mid-word. The ordering ends in an ordinal tiebreak so paging
/// stays deterministic.
/// </summary>
public static class ApiTextRanking
{
    /// <summary>
    /// Returns <paramref name="hits"/> ordered most-informative first for <paramref name="query"/>.
    /// </summary>
    public static IReadOnlyList<ApiTextHit> Order(IEnumerable<ApiTextHit> hits, string query)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(query);

        return hits
            .OrderBy(hit => ElementRank(hit.Element))
            .ThenBy(hit => WholeWordRank(hit.Text, query))
            .ThenBy(hit => hit.Symbol, StringComparer.Ordinal)
            .ThenBy(hit => hit.Element, StringComparer.Ordinal)
            .ThenBy(hit => hit.Text, StringComparer.Ordinal)
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Keeps at most <paramref name="perSymbolLimit"/> hits per owning symbol, in the order given,
    /// so one heavily-documented symbol cannot crowd every other API off the page. The last kept hit
    /// of a symbol that overflowed carries the dropped count in
    /// <see cref="ApiTextHit.MoreFromSymbol"/>; lookup_api on that symbol reaches the rest.
    /// </summary>
    /// <remarks>
    /// Runs on the already-ranked, whole result set before paging, so the collapsed sequence is a
    /// stable total order and an offset cursor keeps addressing the same set across pages.
    /// </remarks>
    public static IReadOnlyList<ApiTextHit> CollapsePerSymbol(
        IReadOnlyList<ApiTextHit> hits,
        int perSymbolLimit)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentOutOfRangeException.ThrowIfLessThan(perSymbolLimit, 1);

        var totalPerSymbol = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var hit in hits)
            totalPerSymbol[hit.Symbol] = totalPerSymbol.GetValueOrDefault(hit.Symbol) + 1;

        var keptPerSymbol = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new List<ApiTextHit>(hits.Count);
        foreach (var hit in hits)
        {
            var soFar = keptPerSymbol.GetValueOrDefault(hit.Symbol);
            if (soFar >= perSymbolLimit)
                continue;

            var isLastKept = soFar == perSymbolLimit - 1;
            var dropped = totalPerSymbol[hit.Symbol] - perSymbolLimit;
            kept.Add(isLastKept && dropped > 0 ? hit with { MoreFromSymbol = dropped } : hit);
            keptPerSymbol[hit.Symbol] = soFar + 1;
        }

        return kept;
    }

    // A summary is the entry's headline; returns and remarks expand on it; a parameter or exception
    // clause is the finest detail. The param element is labeled "param:name", so match by prefix.
    private static int ElementRank(string element)
    {
        if (element.Equals("summary", StringComparison.Ordinal))
            return 0;
        if (element.Equals("returns", StringComparison.Ordinal))
            return 1;
        if (element.Equals("remarks", StringComparison.Ordinal))
            return 2;
        if (element.Equals("value", StringComparison.Ordinal))
            return 3;
        if (element.StartsWith("param", StringComparison.Ordinal)
            || element.StartsWith("typeparam", StringComparison.Ordinal))
            return 4;
        if (element.Equals("exception", StringComparison.Ordinal))
            return 5;
        return 6;
    }

    // 0 when the query sits on word boundaries in the text, 1 when it is a mid-word substring.
    private static int WholeWordRank(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + query.Length;
            var afterIsBoundary = afterIndex == text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (beforeIsBoundary && afterIsBoundary)
                return 0;

            index = text.IndexOf(query, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return 1;
    }
}
