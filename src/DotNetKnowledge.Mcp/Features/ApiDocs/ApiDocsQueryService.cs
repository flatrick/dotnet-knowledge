using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

public sealed class ApiDocsQueryService
{
    /// <summary>
    /// Matches one symbol contributes to a text-search result set before the rest are folded behind a
    /// <c>moreFromSymbol</c> count. Two carries the headline and its leading detail without letting a
    /// symbol whose every element mentions the query crowd out every other API.
    /// </summary>
    private const int TextHitsPerSymbol = 2;

    private readonly SourceCatalog _catalog;
    private readonly SourceSynchronizer _synchronizer;

    public ApiDocsQueryService(
        SourceCatalog catalog,
        SourceCache cache,
        SourceSynchronizer synchronizer)
    {
        _catalog = catalog;
        ArgumentNullException.ThrowIfNull(cache);
        _synchronizer = synchronizer;
    }

    public async Task<ApiLookupResult> LookupAsync(
        string symbol,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ValidateSymbol(symbol);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");
        var sourceNames = ResolveSourceNames(source);
        var matches = new List<ApiLookupTypeRead>();
        var resolvedTypeNames = new List<string>();
        var searchedSources = new List<ApiProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApiLookupRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot => new RepositoryApiDocsBackend(sourceName, snapshot)
                        .Lookup(symbol, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.AddRange(read.Coverage.SearchedSources);
            matches.AddRange(read.Matches);
            resolvedTypeNames.AddRange(read.ResolvedTypeNames);
        }

        var ordered = matches
            .OrderBy(match => match.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Source.RevisionKey, StringComparer.Ordinal)
            .ToArray();
        var outcome = ordered.Length > 0
            ? ApiLookupOutcome.Found
            : resolvedTypeNames.Count > 0
                ? ApiLookupOutcome.MemberNotFound
                : ApiLookupOutcome.TypeNotFound;
        var distinctTypeNames = resolvedTypeNames.Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Paging runs over one flat, ordinally-ordered member sequence across every match, so a
        // three-type result such as List has one pagination state rather than three. A type with
        // no members (a bare-type-name match against a marker type, or a namesake pair like
        // Holder/Holder<T> where one arity carries no documented members) still occupies one slot
        // in the sequence via a null placeholder — otherwise it would contribute nothing to any
        // page and silently disappear from every response.
        var pairs = ordered
            .SelectMany(type => type.Members.Count > 0
                ? type.Members
                    .OrderBy(member => member.Name, StringComparer.Ordinal)
                    .ThenBy(member => member.Signature, StringComparer.Ordinal)
                    .Select(member => (Type: type, Member: (ApiLookupMemberRead?)member))
                : [(Type: type, Member: (ApiLookupMemberRead?)null)])
            .ToArray();

        var revisions = searchedSources
            .Select(searched => searched.RevisionKey)
            .ToArray();
        var offset = DecodeCursor(cursor, "lookup", symbol, revisions);
        if (offset > pairs.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = pairs.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < pairs.Length;

        var paged = page
            .GroupBy(pair => (pair.Type.FullName, pair.Type.Source.RevisionKey))
            .Select(group =>
            {
                var type = group.First().Type;
                var members = group
                    .Where(pair => pair.Member is not null)
                    .Select(pair => type.Detail == ApiLookupDetail.Signatures
                        ? ToSignature(pair.Member!.Documentation)
                        : pair.Member!.Documentation)
                    .ToArray();
                return type.Documentation with { Members = members };
            })
            .ToArray();

        return new ApiLookupResult(
            paged,
            searchedSources,
            outcome,
            distinctTypeNames,
            isPartial,
            isPartial ? EncodeCursor("lookup", symbol, nextOffset, revisions) : null);
    }

    private static ApiMemberDocumentation ToSignature(ApiMemberDocumentation member) =>
        new(member.Name, member.Signature, Summary: null, Parameters: null, Returns: null, Remarks: null);

    public async Task<ApiSearchResult> SearchAsync(
        string pattern,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        var items = new List<ApiSearchItem>();
        var searchedSources = new List<ApiProvenance>();
        foreach (var sourceName in ResolveSourceNames(source: null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApiSearchRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot => new RepositoryApiDocsBackend(sourceName, snapshot)
                        .Search(pattern, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.AddRange(read.Coverage.SearchedSources);
            items.AddRange(read.Items);
        }

        var revisions = searchedSources
            .Select(source => source.RevisionKey)
            .ToArray();
        var offset = DecodeCursor(cursor, "search", pattern, revisions);

        var ordered = ApiSearchRanking.Order(items, pattern);
        if (offset > ordered.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Count;
        return new ApiSearchResult(
            Items: page,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("search", pattern, nextOffset, revisions) : null,
            SearchedSources: searchedSources,
            Note: DottedMissNote(pattern, ordered.Count));
    }

    // search_api matches type names, not members: a Type.Member name, or a generic type's full name
    // shorn of its `arity, matches nothing and returns an empty set that reads as "no such API". When
    // a dotted pattern of non-empty segments finds nothing, name the calls that would reach it.
    private static ApiSearchNote? DottedMissNote(string pattern, int matchCount)
    {
        if (matchCount > 0)
            return null;

        var segments = pattern.Split('.');
        if (segments.Length < 2 || Array.Exists(segments, segment => segment.Length == 0))
            return null;

        var leaf = segments[^1];
        return new ApiSearchNote(
            $"No type name matched '{pattern}'. If it names a member, call "
            + $"lookup_api(symbol: \"{pattern}\"). If it is a generic type's full name, search its "
            + $"simple name: search_api(pattern: \"{leaf}\").");
    }

    /// <summary>
    /// Searches the prose inside the API documentation — summaries, remarks, returns, and parameter
    /// descriptions — rather than names.
    /// </summary>
    /// <remarks>
    /// This is the question <see cref="LookupAsync"/> structurally cannot serve: it takes the name
    /// as input, and the caller here has only a behavior in mind. Matching is a literal
    /// case-insensitive substring against the rendered element text, so what was searched is what
    /// comes back; regex is deliberately not offered, because the cheap prefilter that makes a
    /// whole-corpus scan affordable cannot be a sound superset of an arbitrary pattern.
    /// </remarks>
    public async Task<ApiTextSearchResult> SearchTextAsync(
        string query,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        var hits = new List<ApiTextHit>();
        var searchedSources = new List<ApiProvenance>();
        foreach (var sourceName in ResolveSourceNames(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApiTextRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot => new RepositoryApiDocsBackend(sourceName, snapshot)
                        .SearchText(query, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.AddRange(read.Coverage.SearchedSources);
            hits.AddRange(read.Hits.Select(hit => hit.Hit));
        }

        var revisions = searchedSources
            .Select(item => item.RevisionKey)
            .ToArray();
        // Serialized rather than concatenated, so a query ending in the source name cannot
        // forge the scope of a different request.
        var scope = JsonSerializer.Serialize(new[] { query, source ?? string.Empty });
        var offset = DecodeCursor(cursor, "search-text", scope, revisions);

        // Overloads each carry their own Docs under one MemberName, so identical prose on
        // Create(a) and Create(a, b) would otherwise arrive as two hits a caller cannot tell apart.
        // Prose that genuinely differs between overloads survives, because the text is part of the
        // key.
        var deduplicated = hits
            .DistinctBy(hit => (hit.Symbol, hit.Element, hit.Text, hit.Source.RevisionKey));
        var ordered = ApiTextRanking.CollapsePerSymbol(
            ApiTextRanking.Order(deduplicated, query),
            TextHitsPerSymbol);
        if (offset > ordered.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Count;
        return new ApiTextSearchResult(
            Hits: page,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("search-text", scope, nextOffset, revisions) : null,
            SearchedSources: searchedSources);
    }

    public async Task<ApiReferenceResult> FindReferencesAsync(
        string symbol,
        string? kind,
        bool? exact,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ValidateSymbol(symbol);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");
        if (kind is not null && !ApiReferenceKind.All.Contains(kind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"kind must be omitted or one of \"{string.Join("\", \"", ApiReferenceKind.All)}\".",
                nameof(kind));
        }

        var hits = new List<ApiReferenceHit>();
        var searchedSources = new List<ApiProvenance>();
        string? siblingType = null;
        var siblingApplications = 0;
        foreach (var sourceName in ResolveSourceNames(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApiReferenceRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot => new RepositoryApiDocsBackend(sourceName, snapshot)
                        .FindReferences(symbol, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.AddRange(read.Coverage.SearchedSources);
            hits.AddRange(read.Items.Select(hit => hit.Hit));

            // One source resolving the sibling is enough to name it; the counts add up across the
            // sources that were actually searched.
            siblingType ??= read.SiblingType;
            siblingApplications += read.SiblingApplications;
        }

        // Totals describe the whole matched set before the filters narrow it, so a caller
        // asking only for parameters can still see that a thousand types derive from this one.
        var totals = new ApiReferenceTotals(
            Parameter: hits.Count(hit => hit.Kind == ApiReferenceKind.Parameter),
            Return: hits.Count(hit => hit.Kind == ApiReferenceKind.Return),
            Base: hits.Count(hit => hit.Kind == ApiReferenceKind.Base),
            Interface: hits.Count(hit => hit.Kind == ApiReferenceKind.Interface),
            Constraint: hits.Count(hit => hit.Kind == ApiReferenceKind.Constraint),
            Attribute: hits.Count(hit => hit.Kind == ApiReferenceKind.Attribute));

        var revisions = searchedSources
            .Select(item => item.RevisionKey)
            .ToArray();
        var scope = JsonSerializer.Serialize(
            new[] { symbol, kind ?? string.Empty, exact?.ToString() ?? string.Empty, source ?? string.Empty });
        var offset = DecodeCursor(cursor, "references", scope, revisions);

        var ordered = hits
            .Where(hit => kind is null || hit.Kind == kind)
            .Where(hit => exact is null || hit.IsExact == exact)
            .OrderBy(hit => hit.Symbol, StringComparer.Ordinal)
            .ThenBy(hit => hit.Kind, StringComparer.Ordinal)
            .ThenBy(hit => hit.ParameterName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(hit => hit.TypeExpression ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(hit => hit.Source.RevisionKey, StringComparer.Ordinal)
            .ToArray();
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;
        return new ApiReferenceResult(
            Hits: page,
            Totals: totals,
            Note: siblingType is null
                ? null
                : new ApiReferenceNote(
                    siblingType,
                    siblingApplications,
                    $"Call find_api_references with symbol \"{siblingType}\" to reach its attribute "
                        + "applications, which are excluded here."),
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("references", scope, nextOffset, revisions) : null,
            SearchedSources: searchedSources);
    }

    private string[] ResolveSourceNames(string? source)
    {
        if (source is not null)
        {
            if (!RepositoryApiDocsBackend.Supports(source) || !_catalog.Sources.ContainsKey(source))
            {
                throw new ArgumentException(
                    "source must be \"dotnet-api-docs\" or \"roslyn-api-docs\".",
                    nameof(source));
            }

            return [source];
        }

        return RepositoryApiDocsBackend.SourceNames
            .Where(_catalog.Sources.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSymbol(string symbol)
    {
        if (Path.IsPathRooted(symbol)
            || symbol.IndexOfAny(['/', '\\', ':', '*', '?', '[', ']']) >= 0
            || symbol.Split('.').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new ArgumentException("symbol contains invalid path or wildcard characters.", nameof(symbol));
        }
    }

    internal static string EncodeCursor(string kind, string scope, int offset, IReadOnlyList<string> revisions)
    {
        var json = JsonSerializer.Serialize(
            new PageCursor(Version: 1, Kind: kind, Scope: scope, Offset: offset, Revisions: revisions));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursor(string? cursor, string kind, string scope, IReadOnlyList<string> revisions)
    {
        if (cursor is null)
            return 0;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<PageCursor>(
                Encoding.UTF8.GetString(Convert.FromBase64String(base64)));

            // Kind keeps a search cursor from being honored by a lookup; scope keeps a cursor for
            // one symbol or pattern from being honored for another; revisions keep any cursor from
            // surviving a re-synchronization that changes what it points at.
            if (decoded is null
                || decoded.Version != 1
                || decoded.Offset < 0
                || !string.Equals(decoded.Kind, kind, StringComparison.Ordinal)
                || !string.Equals(decoded.Scope, scope, StringComparison.Ordinal)
                || decoded.Revisions is null
                || !decoded.Revisions.SequenceEqual(revisions, StringComparer.Ordinal))
            {
                throw new ArgumentException("cursor does not match this request.", nameof(cursor));
            }

            return decoded.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("cursor is invalid.", nameof(cursor), exception);
        }
    }

    private sealed record PageCursor(
        int Version,
        string Kind,
        string Scope,
        int Offset,
        IReadOnlyList<string> Revisions);

}
