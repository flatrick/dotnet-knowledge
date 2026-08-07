namespace DotNetKnowledge.Mcp.Features.ApiDocs;

/// <summary>
/// Orders <see cref="ApiSearchItem"/> matches by relevance to the pattern rather than by name alone.
/// A caller searching "Span" wants <c>System.Span&lt;T&gt;</c> ahead of every Roslyn type that merely
/// contains the substring, and an ordinal sort buries it. The ordering is a total, deterministic one —
/// relevance keys first, then the ordinal name and repository as the final tiebreak — so an
/// offset-based cursor keeps addressing the same result set across pages.
/// </summary>
public static class ApiSearchRanking
{
    /// <summary>
    /// Returns <paramref name="items"/> ordered most-relevant first for <paramref name="pattern"/>.
    /// </summary>
    public static IReadOnlyList<ApiSearchItem> Order(IEnumerable<ApiSearchItem> items, string pattern)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pattern);

        var leaf = LastSegment(pattern);
        return items
            .OrderBy(item => MatchTier(item.MatchedOn))
            .ThenBy(item => item.NamespaceDepth ?? 0)
            .ThenBy(item => NameTier(item, leaf))
            .ThenBy(item => NamespaceSegmentCount(item.Name))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Source.Repo, StringComparer.Ordinal)
            .ToArray();
    }

    // A whole-name match is the strongest signal the caller found what they named; a type-name match
    // is next; a namespace match answers a broader question and ranks last.
    private static int MatchTier(string matchedOn) => matchedOn switch
    {
        ApiNameMatch.FullName => 0,
        ApiNameMatch.Type => 1,
        ApiNameMatch.Namespace => 2,
        _ => 3,
    };

    // How the simple type name relates to the pattern's trailing segment: an exact name beats a
    // prefix, which beats a substring buried mid-name. Neutral for namespace matches, whose order is
    // carried by depth rather than by the type name.
    private static int NameTier(ApiSearchItem item, string leaf)
    {
        if (item.MatchedOn == ApiNameMatch.Namespace)
            return 0;

        var simpleName = StripGenericArity(LastSegment(item.Name));
        if (simpleName.Equals(leaf, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (simpleName.StartsWith(leaf, StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    // ECMA XML spells a generic type Span`1, so its simple name never equals the base "Span" a caller
    // types. Drop a trailing `arity (Span`1 → Span) so the generic type reads as the exact match it
    // is; leave a mid-name backtick alone, as in the nested Span`1+Enumerator, which is not one.
    private static string StripGenericArity(string simpleName)
    {
        var tick = simpleName.LastIndexOf('`');
        if (tick <= 0 || tick == simpleName.Length - 1)
            return simpleName;

        for (var index = tick + 1; index < simpleName.Length; index++)
        {
            if (!char.IsDigit(simpleName[index]))
                return simpleName;
        }

        return simpleName[..tick];
    }

    // A shallower namespace is the more mainline home for a name, so System.Span outranks a
    // like-named type nested under Microsoft.CodeAnalysis.CSharp.Syntax when both match equally well.
    private static int NamespaceSegmentCount(string name)
    {
        var count = 0;
        foreach (var character in name)
        {
            if (character == '.')
                count++;
        }

        return count;
    }

    private static string LastSegment(string name)
    {
        var lastDot = name.LastIndexOf('.');
        return lastDot < 0 ? name : name[(lastDot + 1)..];
    }
}
