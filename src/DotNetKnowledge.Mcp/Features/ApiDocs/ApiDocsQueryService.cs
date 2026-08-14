using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;
using DotNetKnowledge.Mcp.Text;

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

    public Task<ApiLookupResult> LookupAsync(
        string symbol,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken) =>
        LookupCoreAsync(
            symbol, source, framework: null, limit, cursor, legacyCursorScope: true, cancellationToken);

    public async Task<ApiLookupResult> LookupAsync(
        string symbol,
        string? source,
        string? framework,
        int limit,
        string? cursor,
        CancellationToken cancellationToken) =>
        await LookupCoreAsync(
            symbol, source, framework, limit, cursor, legacyCursorScope: false, cancellationToken)
            .ConfigureAwait(false);

    private async Task<ApiLookupResult> LookupCoreAsync(
        string symbol,
        string? source,
        string? framework,
        int limit,
        string? cursor,
        bool legacyCursorScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ValidateSymbol(symbol);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");
        var sourceNames = ResolveSourceNames(source, framework);
        var composition = await ReadComposedAsync(
            sourceNames,
            framework,
            backend => backend.Lookup(symbol, cancellationToken),
            read => read.Coverage,
            cancellationToken).ConfigureAwait(false);
        var includedDeclarations = composition.Reads
            .SelectMany(read => read.Matches)
            .Select(match => match.DeclarationId)
            .ToHashSet(StringComparer.Ordinal);
        var matches = composition.Reads
            .SelectMany(read => read.ResolvedTypes)
            .GroupBy(match => match.DeclarationId, StringComparer.Ordinal)
            .Where(group => includedDeclarations.Contains(group.Key))
            .Select(group => MergeLookupType(group))
            .ToArray();
        var resolvedTypeNames = composition.Reads
            .SelectMany(read => read.ResolvedTypeNames)
            .ToArray();

        var ordered = matches
            .OrderBy(match => match.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Source.RevisionKey, StringComparer.Ordinal)
            .ToArray();
        var outcome = ordered.Length > 0
            ? ApiLookupOutcome.Found
            : resolvedTypeNames.Length > 0
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

        var revisions = composition.SearchedSources
            .Select(searched => searched.RevisionKey)
            .ToArray();
        var scope = legacyCursorScope
            ? symbol
            : RequestScope(symbol, source, composition.EffectiveFramework);
        var offset = DecodeCursor(cursor, "lookup", scope, revisions);
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
            composition.SearchedSources,
            outcome,
            distinctTypeNames,
            isPartial,
            isPartial ? EncodeCursor("lookup", scope, nextOffset, revisions) : null,
            composition.EffectiveFramework,
            composition.DefaultFramework,
            composition.AvailableFrameworks,
            composition.SkippedDeclarations);
    }

    private static ApiMemberDocumentation ToSignature(ApiMemberDocumentation member) =>
        new(
            member.Name,
            member.Signature,
            Summary: null,
            Parameters: null,
            Returns: null,
            Remarks: null,
            member.Source);

    private static ApiLookupTypeRead MergeLookupType(IEnumerable<ApiLookupTypeRead> declarations)
    {
        var candidates = declarations.ToArray();
        var winner = candidates[0];
        var members = candidates
            .SelectMany(candidate => candidate.Members)
            .DistinctBy(member => member.DeclarationId, StringComparer.Ordinal)
            .ToArray();
        return new ApiLookupTypeRead(
            winner.DeclarationId,
            winner.Documentation with
            {
                Members = members.Select(member => member.Documentation).ToArray(),
            },
            members);
    }

    public Task<ApiSearchResult> SearchAsync(
        string pattern,
        int limit,
        string? cursor,
        CancellationToken cancellationToken) =>
        SearchAsync(pattern, framework: null, limit, cursor, cancellationToken);

    public async Task<ApiSearchResult> SearchAsync(
        string pattern,
        string? framework,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        var composition = await ReadComposedAsync(
            ResolveSourceNames(source: null, framework),
            framework,
            backend => backend.Search(pattern, cancellationToken),
            read => read.Coverage,
            cancellationToken).ConfigureAwait(false);
        var items = composition.Reads
            .SelectMany(read => read.Items)
            .DistinctBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var revisions = composition.SearchedSources
            .Select(source => source.RevisionKey)
            .ToArray();
        var scope = RequestScope(pattern, composition.EffectiveFramework);
        var offset = DecodeCursor(cursor, "search", scope, revisions);

        var ordered = ApiSearchRanking.Order(items, pattern);
        if (offset > ordered.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Count;
        return new ApiSearchResult(
            Items: page,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("search", scope, nextOffset, revisions) : null,
            SearchedSources: composition.SearchedSources,
            Note: DottedMissNote(pattern, ordered.Count),
            EffectiveFramework: composition.EffectiveFramework,
            DefaultFramework: composition.DefaultFramework,
            AvailableFrameworks: composition.AvailableFrameworks,
            SkippedDeclarations: composition.SkippedDeclarations);
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
    public Task<ApiTextSearchResult> SearchTextAsync(
        string query,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken) =>
        SearchTextAsync(query, source, framework: null, limit, cursor, cancellationToken);

    public async Task<ApiTextSearchResult> SearchTextAsync(
        string query,
        string? source,
        string? framework,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        var composition = await ReadComposedAsync(
            ResolveSourceNames(source, framework),
            framework,
            backend => backend.SearchText(query, cancellationToken),
            read => read.Coverage,
            cancellationToken).ConfigureAwait(false);

        var revisions = composition.SearchedSources
            .Select(item => item.RevisionKey)
            .ToArray();
        // Serialized rather than concatenated, so a query ending in the source name cannot
        // forge the scope of a different request.
        var scope = RequestScope(query, source, composition.EffectiveFramework);
        var offset = DecodeCursor(cursor, "search-text", scope, revisions);

        // Overloads each carry their own Docs under one MemberName, so identical prose on
        // Create(a) and Create(a, b) would otherwise arrive as two hits a caller cannot tell apart.
        // Prose that genuinely differs between overloads survives, because the text is part of the
        // key.
        var deduplicated = composition.Reads
            .SelectMany(read => read.Hits)
            .DistinctBy(hit => (
                hit.DeclarationId,
                hit.Element,
                Text: DocumentationText.Normalize(hit.Text, collapseWhitespace: true) ?? string.Empty))
            .Select(hit => hit.Hit);
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
            SearchedSources: composition.SearchedSources,
            EffectiveFramework: composition.EffectiveFramework,
            DefaultFramework: composition.DefaultFramework,
            AvailableFrameworks: composition.AvailableFrameworks,
            SkippedDeclarations: composition.SkippedDeclarations);
    }

    public Task<ApiReferenceResult> FindReferencesAsync(
        string symbol,
        string? kind,
        bool? exact,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken) =>
        FindReferencesAsync(symbol, kind, exact, source, framework: null, limit, cursor, cancellationToken);

    public async Task<ApiReferenceResult> FindReferencesAsync(
        string symbol,
        string? kind,
        bool? exact,
        string? source,
        string? framework,
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

        var composition = await ReadComposedAsync(
            ResolveSourceNames(source, framework),
            framework,
            backend => backend.FindReferences(symbol, cancellationToken),
            read => read.Coverage,
            cancellationToken).ConfigureAwait(false);
        var deduplicated = composition.Reads
            .SelectMany(read => read.Items)
            .DistinctBy(hit => (
                hit.DeclarationId,
                hit.Kind,
                ParameterName: hit.ParameterName ?? string.Empty,
                TypeExpression: hit.TypeExpression ?? string.Empty))
            .ToArray();
        var hits = deduplicated.Select(hit => hit.Hit).ToArray();
        string? siblingType = null;
        var siblingApplications = 0;
        foreach (var read in composition.Reads)
        {
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

        var revisions = composition.SearchedSources
            .Select(item => item.RevisionKey)
            .ToArray();
        var scope = RequestScope(
            symbol,
            kind,
            exact?.ToString(),
            source,
            composition.EffectiveFramework);
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
            SearchedSources: composition.SearchedSources,
            EffectiveFramework: composition.EffectiveFramework,
            DefaultFramework: composition.DefaultFramework,
            AvailableFrameworks: composition.AvailableFrameworks,
            SkippedDeclarations: composition.SkippedDeclarations);
    }

    private string[] ResolveSourceNames(string? source, string? framework)
    {
        if (source is not null)
        {
            if (!RepositoryApiDocsBackend.Supports(source) || !_catalog.Sources.ContainsKey(source))
            {
                throw new ArgumentException(
                    "source must be \"dotnet-api-docs\" or \"roslyn-api-docs\".",
                    nameof(source));
            }
            if (framework is not null
                && string.Equals(source, "dotnet-api-docs", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "framework applies only to the roslyn-api-docs package supplement.",
                    nameof(framework));
            }

            return [source];
        }

        return RepositoryApiDocsBackend.SourceNames
            .Where(_catalog.Sources.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<ComposedReads<T>> ReadComposedAsync<T>(
        IReadOnlyList<string> sourceNames,
        string? requestedFramework,
        Func<IApiDocsBackend, T> reader,
        Func<T, ApiQueryCoverage> coverage,
        CancellationToken cancellationToken)
        where T : class
    {
        var repositoryReads = new List<T>();
        var packageReads = new List<T>();
        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceReads<T> sourceReads;
            try
            {
                sourceReads = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot =>
                    {
                        var repository = reader(new RepositoryApiDocsBackend(sourceName, snapshot));
                        var packages = new List<T>();
                        if (string.Equals(sourceName, "roslyn-api-docs", StringComparison.Ordinal))
                        {
                            foreach (var package in OrderPackages(snapshot.Definition.ApiPackages ?? []))
                            {
                                if (package is null)
                                    throw new InvalidDataException("The API package definition cannot be null.");
                                var framework = ResolveFramework(snapshot, package.PackageId, requestedFramework);
                                packages.Add(reader(new PackageApiDocsBackend(
                                    snapshot, package.PackageId, framework)));
                            }
                        }
                        return new SourceReads<T>(repository, packages);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            repositoryReads.Add(sourceReads.Repository);
            packageReads.AddRange(sourceReads.Packages);
        }

        var reads = repositoryReads.Concat(packageReads).ToArray();
        var searchedSources = reads
            .SelectMany(read => coverage(read).SearchedSources)
            .ToArray();
        var packageCoverages = packageReads.Select(coverage).ToArray();
        if (packageCoverages.Length == 0)
        {
            return new ComposedReads<T>(
                reads, searchedSources, null, null, null, null);
        }

        var frameworkCoverage = packageCoverages[0];
        if (packageCoverages.Skip(1).Any(item =>
                !string.Equals(
                    item.EffectiveFramework,
                    frameworkCoverage.EffectiveFramework,
                    StringComparison.Ordinal)
                || !string.Equals(
                    item.DefaultFramework,
                    frameworkCoverage.DefaultFramework,
                    StringComparison.Ordinal)
                || !(item.AvailableFrameworks ?? []).SequenceEqual(
                    frameworkCoverage.AvailableFrameworks ?? [],
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                "Participating API package supplements do not expose the same framework selection.");
        }

        return new ComposedReads<T>(
            reads,
            searchedSources,
            frameworkCoverage.EffectiveFramework,
            frameworkCoverage.DefaultFramework,
            frameworkCoverage.AvailableFrameworks,
            MergeSkipped(packageCoverages));
    }

    /// <summary>
    /// Sums the skip reports of every participating package supplement. A merged answer is drawn
    /// from every corpus that took part, so its coverage gap is their union, not any one of theirs.
    /// </summary>
    private static ApiSkipCoverage? MergeSkipped(IReadOnlyList<ApiQueryCoverage> coverages)
    {
        var reports = coverages
            .Select(item => item.SkippedDeclarations)
            .Where(item => item is not null)
            .ToArray();
        if (reports.Length == 0)
            return null;
        if (reports.Length == 1)
            return reports[0];

        var reasons = reports
            .SelectMany(report => report!.Reasons)
            .GroupBy(reason => reason.Reason, StringComparer.Ordinal)
            .Select(group => new ApiSkipReason(group.Key, group.Sum(item => item.Count)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Reason, StringComparer.Ordinal)
            .ToArray();

        return new ApiSkipCoverage(
            reports.Sum(report => report!.Declarations),
            reasons,
            // A merge of partial reason lists is itself partial: the reasons each cap dropped are
            // still missing, and summing the kept ones does not recover them.
            reports.Any(report => report!.ReasonsArePartial));
    }

    private static string ResolveFramework(
        SourceSnapshot snapshot,
        string packageId,
        string? requestedFramework)
    {
        var states = (snapshot.State.ApiPackages ?? [])
            .Where(state => state is not null
                && string.Equals(state.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (states.Length != 1)
        {
            throw new InvalidDataException(
                $"The synchronized snapshot must contain exactly one API package state named '{packageId}'.");
        }

        var state = states[0];
        if (state.AvailableFrameworks is null || state.AvailableFrameworks.Count == 0)
            throw new InvalidDataException("The synchronized API package has no available frameworks.");
        var effective = requestedFramework ?? state.DefaultFramework;
        var canonical = state.AvailableFrameworks.FirstOrDefault(item =>
            string.Equals(item, effective, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            throw new FrameworkNotAvailableException(
                effective,
                state.DefaultFramework,
                state.AvailableFrameworks.ToArray());
        }
        return canonical;
    }

    private static IOrderedEnumerable<ApiPackageDefinition> OrderPackages(
        IEnumerable<ApiPackageDefinition> packages) =>
        packages
            .OrderBy(package => package?.PackageId, StringComparer.Ordinal)
            .ThenBy(package => package?.AssemblyName, StringComparer.Ordinal)
            .ThenBy(package => package?.Version, StringComparer.Ordinal)
            .ThenBy(package => package?.Sha512, StringComparer.Ordinal)
            .ThenBy(package => package?.Feed, StringComparer.Ordinal)
            .ThenBy(package => package?.DefaultFramework, StringComparer.Ordinal);

    internal static string RequestScope(params string?[] values) =>
        JsonSerializer.Serialize(values.Select(value => value ?? string.Empty));

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

    private sealed record SourceReads<T>(T Repository, IReadOnlyList<T> Packages)
        where T : class;

    private sealed record ComposedReads<T>(
        IReadOnlyList<T> Reads,
        IReadOnlyList<ApiProvenance> SearchedSources,
        string? EffectiveFramework,
        string? DefaultFramework,
        IReadOnlyList<string>? AvailableFrameworks,
        ApiSkipCoverage? SkippedDeclarations)
        where T : class;

}
