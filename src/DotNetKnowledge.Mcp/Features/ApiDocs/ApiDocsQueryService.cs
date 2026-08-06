using System.Xml.Linq;
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;
using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

public sealed class ApiDocsQueryService
{
    private static readonly IReadOnlyDictionary<string, string[]> ApiRootSegments =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dotnet-api-docs"] = ["xml"],
            ["roslyn-api-docs"] = ["dotnet", "xml"],
        };

    /// <summary>
    /// Characters of matched prose a search hit carries. Enough to judge relevance, not enough to
    /// stand in for the entry; lookup_api is one call away with the symbol the hit names.
    /// </summary>
    private const int MatchTextBudget = 300;

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
        var matches = new List<ApiTypeDocumentation>();
        var resolvedTypeNames = new List<string>();
        var searchedSources = new List<SourceProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LookupRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadLookupSource(
                        sourceName, directory, symbol, definition, state),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            matches.AddRange(read.Matches);
            resolvedTypeNames.AddRange(read.ResolvedTypeNames);
        }

        var ordered = matches
            .OrderBy(match => match.FullName, StringComparer.Ordinal)
            .ThenBy(match => match.Source.Repo, StringComparer.Ordinal)
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
                    .Select(member => (Type: type, Member: (ApiMemberDocumentation?)member))
                : [(Type: type, Member: (ApiMemberDocumentation?)null)])
            .ToArray();

        var revisions = searchedSources
            .Select(searched => searched.Repo + "@" + searched.Ref + "@" + searched.Commit)
            .ToArray();
        var offset = DecodeCursor(cursor, "lookup", symbol, revisions);
        if (offset > pairs.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = pairs.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < pairs.Length;

        var paged = page
            .GroupBy(pair => (pair.Type.FullName, pair.Type.Source.Repo))
            .Select(group => group.First().Type with
            {
                Members = group
                    .Where(pair => pair.Member is not null)
                    .Select(pair => pair.Type.Detail == ApiLookupDetail.Signatures
                        ? ToSignature(pair.Member!)
                        : pair.Member!)
                    .ToArray(),
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
        var searchedSources = new List<SourceProvenance>();
        foreach (var sourceName in ResolveSourceNames(source: null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceRead<ApiSearchItem> read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadSearchSource(
                        sourceName, directory, pattern, definition, state, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            items.AddRange(read.Items);
        }

        var revisions = searchedSources
            .Select(source => source.Repo + "@" + source.Ref + "@" + source.Commit)
            .ToArray();
        var offset = DecodeCursor(cursor, "search", pattern, revisions);

        var ordered = items
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Source.Repo, StringComparer.Ordinal)
            .ToArray();
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;
        return new ApiSearchResult(
            Items: page,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("search", pattern, nextOffset, revisions) : null,
            SearchedSources: searchedSources);
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
        var searchedSources = new List<SourceProvenance>();
        foreach (var sourceName in ResolveSourceNames(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceRead<ApiTextHit> read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadTextSource(
                        sourceName, directory, query, definition, state, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Items);
        }

        var revisions = searchedSources
            .Select(item => item.Repo + "@" + item.Ref + "@" + item.Commit)
            .ToArray();
        // Serialized rather than concatenated, so a query ending in the source name cannot
        // forge the scope of a different request.
        var scope = JsonSerializer.Serialize(new[] { query, source ?? string.Empty });
        var offset = DecodeCursor(cursor, "search-text", scope, revisions);

        // Overloads each carry their own Docs under one MemberName, so identical prose on
        // Create(a) and Create(a, b) would otherwise arrive as two hits a caller cannot tell apart.
        // Prose that genuinely differs between overloads survives, because the text is part of the
        // key.
        var ordered = hits
            .DistinctBy(hit => (hit.Symbol, hit.Element, hit.Text, hit.Source.Repo))
            .OrderBy(hit => hit.Symbol, StringComparer.Ordinal)
            .ThenBy(hit => hit.Element, StringComparer.Ordinal)
            .ThenBy(hit => hit.Text, StringComparer.Ordinal)
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;
        return new ApiTextSearchResult(
            Hits: page,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("search-text", scope, nextOffset, revisions) : null,
            SearchedSources: searchedSources);
    }

    private static SourceRead<ApiTextHit> ReadTextSource(
        string sourceName,
        string directory,
        string query,
        SourceDefinition definition,
        SourceSyncState state,
        CancellationToken cancellationToken)
    {
        var docsRoot = ResolveDocsRoot(sourceName, directory);
        var provenance = ToProvenance(definition, state);
        var prefilter = LongestToken(query);
        var files = Directory.EnumerateFiles(docsRoot, "*.xml", SearchOption.AllDirectories);
        var hits = new System.Collections.Concurrent.ConcurrentBag<ApiTextHit>();

        // The whole corpus is roughly 460 MB. Read in parallel and reject on a raw-text prefilter
        // first: parsing every file would cost orders of magnitude more than reading it, and almost
        // every file is rejected.
        Parallel.ForEach(
            files,
            new ParallelOptions { CancellationToken = cancellationToken },
            file =>
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    return;
                }

                if (!text.Contains(prefilter, StringComparison.OrdinalIgnoreCase))
                    return;

                foreach (var hit in ReadTextHits(file, text, query, provenance))
                    hits.Add(hit);
            });

        return new SourceRead<ApiTextHit>(provenance, hits.ToArray());
    }

    /// <summary>
    /// The longest whitespace-delimited token of the query, which is what the raw-text prefilter
    /// tests.
    /// </summary>
    /// <remarks>
    /// The prefilter has to be a superset of the real match or the search reports plausible
    /// absences, which is the failure this server treats as the dangerous one. It is sound because
    /// every word of the rendered text comes from somewhere in the raw file: either a text node,
    /// which is copied verbatim, or a reference element's attribute, which is where a rendered
    /// symbol name comes from. The full query is not a sound prefilter — rendering closes the gap
    /// an element leaves behind, so "value into a System.String" exists only after rendering.
    /// </remarks>
    private static string LongestToken(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? query : tokens.MaxBy(token => token.Length)!;
    }

    private static IEnumerable<ApiTextHit> ReadTextHits(
        string path,
        string text,
        string query,
        SourceProvenance provenance)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(text).Root;
        }
        catch (System.Xml.XmlException)
        {
            // One malformed file must not fail a whole-corpus search.
            yield break;
        }

        if (root is null)
            yield break;

        var fullName = root.Attribute("FullName")?.Value
            ?? root.Attribute("Name")?.Value
            ?? Path.GetFileNameWithoutExtension(path);

        foreach (var hit in MatchDocs(root.Element("Docs"), fullName, query, provenance))
            yield return hit;

        foreach (var member in root.Descendants("Member"))
        {
            var memberName = member.Attribute("MemberName")?.Value;
            var symbol = memberName is null ? fullName : $"{fullName}.{memberName}";
            foreach (var hit in MatchDocs(member.Element("Docs"), symbol, query, provenance))
                yield return hit;
        }
    }

    private static IEnumerable<ApiTextHit> MatchDocs(
        XElement? docs,
        string symbol,
        string query,
        SourceProvenance provenance)
    {
        if (docs is null)
            yield break;

        foreach (var element in docs.Elements())
        {
            var name = element.Name.LocalName;
            // Anything documented is searchable. Leaving remarks out would keep responses smaller
            // and would answer "no" to questions whose answer is in the corpus.
            if (name is not ("summary" or "remarks" or "returns" or "param" or "typeparam" or "value" or "exception"))
                continue;

            var rendered = RenderDocumentation(element);
            if (rendered is null || !rendered.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            var label = element.Attribute("name")?.Value is { Length: > 0 } parameterName
                ? $"{name}:{parameterName}"
                : name;
            var (text, isTruncated) = DocumentationText.Budget(rendered, MatchTextBudget);
            yield return new ApiTextHit(
                Symbol: symbol,
                Element: label,
                Text: text,
                IsTruncated: isTruncated,
                Source: provenance);
        }
    }

    /// <summary>
    /// Finds declarations that use a type structurally — as a parameter, as a return type, as a
    /// base class, in an interface list, as a generic constraint, or in an attribute application.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="LookupAsync"/>: not "what does this type offer" but "what uses it".
    /// Prose mentions are <c>search_api_text</c>'s job and are deliberately not counted here, since
    /// a sentence naming a type and a signature accepting one are different facts.
    /// </remarks>
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
        var searchedSources = new List<SourceProvenance>();
        foreach (var sourceName in ResolveSourceNames(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceRead<ApiReferenceHit> read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadReferenceSource(
                        sourceName, directory, symbol, definition, state, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Items);
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
            .Select(item => item.Repo + "@" + item.Ref + "@" + item.Commit)
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
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;
        return new ApiReferenceResult(
            Hits: page,
            Totals: totals,
            IsPartial: isPartial,
            NextPageToken: isPartial ? EncodeCursor("references", scope, nextOffset, revisions) : null,
            SearchedSources: searchedSources);
    }

    private static SourceRead<ApiReferenceHit> ReadReferenceSource(
        string sourceName,
        string directory,
        string symbol,
        SourceDefinition definition,
        SourceSyncState state,
        CancellationToken cancellationToken)
    {
        var docsRoot = ResolveDocsRoot(sourceName, directory);
        var provenance = ToProvenance(definition, state);
        var files = Directory.EnumerateFiles(docsRoot, "*.xml", SearchOption.AllDirectories);
        var hits = new System.Collections.Concurrent.ConcurrentBag<ApiReferenceHit>();

        // The whole symbol is a sound prefilter here, unlike prose: a structural reference always
        // spells the type out in an attribute or element, with no rendering step in between.
        Parallel.ForEach(
            files,
            new ParallelOptions { CancellationToken = cancellationToken },
            file =>
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    return;
                }

                if (!text.Contains(symbol, StringComparison.Ordinal))
                    return;

                foreach (var hit in ReadReferenceHits(file, text, symbol, provenance))
                    hits.Add(hit);
            });

        return new SourceRead<ApiReferenceHit>(provenance, hits.ToArray());
    }

    private static IEnumerable<ApiReferenceHit> ReadReferenceHits(
        string path,
        string text,
        string symbol,
        SourceProvenance provenance)
    {
        XElement? root;
        try
        {
            root = XDocument.Parse(text).Root;
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }

        if (root is null)
            yield break;

        var fullName = root.Attribute("FullName")?.Value
            ?? root.Attribute("Name")?.Value
            ?? Path.GetFileNameWithoutExtension(path);

        // A type never counts as a reference to itself.
        if (string.Equals(fullName, symbol, StringComparison.Ordinal))
            yield break;

        var baseTypeName = root.Element("Base")?.Element("BaseTypeName")?.Value;
        if (baseTypeName is not null && ReferencesType(baseTypeName, symbol, out var baseIsExact))
        {
            yield return new ApiReferenceHit(
                fullName, ApiReferenceKind.Base, null, baseTypeName, baseIsExact, null, provenance);
        }

        foreach (var interfaceName in root.Element("Interfaces")?.Elements("Interface")
                     .Select(item => item.Element("InterfaceName")?.Value) ?? [])
        {
            if (interfaceName is not null && ReferencesType(interfaceName, symbol, out var interfaceIsExact))
            {
                yield return new ApiReferenceHit(
                    fullName, ApiReferenceKind.Interface, null, interfaceName, interfaceIsExact, null, provenance);
            }
        }

        foreach (var hit in ReadConstraintHits(root, symbol, fullName, null, provenance))
            yield return hit;

        foreach (var hit in ReadAttributeHits(root, symbol, fullName, null, provenance))
            yield return hit;

        foreach (var member in root.Descendants("Member"))
        {
            var memberName = member.Attribute("MemberName")?.Value;
            var memberSymbol = memberName is null ? fullName : $"{fullName}.{memberName}";
            var signature = member.Elements("MemberSignature")
                .LastOrDefault(element => string.Equals(element.Attribute("Language")?.Value, "C#", StringComparison.Ordinal))
                ?.Attribute("Value")?.Value;

            foreach (var hit in ReadConstraintHits(member, symbol, memberSymbol, signature, provenance))
                yield return hit;

            foreach (var hit in ReadAttributeHits(member, symbol, memberSymbol, signature, provenance))
                yield return hit;

            var returnType = member.Element("ReturnValue")?.Element("ReturnType")?.Value;
            if (returnType is not null && ReferencesType(returnType, symbol, out var returnIsExact))
            {
                yield return new ApiReferenceHit(
                    memberSymbol, ApiReferenceKind.Return, null, returnType, returnIsExact, signature, provenance);
            }

            foreach (var parameter in member.Element("Parameters")?.Elements("Parameter") ?? [])
            {
                var parameterType = parameter.Attribute("Type")?.Value;
                if (parameterType is not null && ReferencesType(parameterType, symbol, out var parameterIsExact))
                {
                    yield return new ApiReferenceHit(
                        memberSymbol,
                        ApiReferenceKind.Parameter,
                        parameter.Attribute("Name")?.Value,
                        parameterType,
                        parameterIsExact,
                        signature,
                        provenance);
                }
            }
        }
    }

    /// <summary>
    /// Constraint references on one declaration's own type parameters — <c>where T : Stream</c> —
    /// which live in a <c>TypeParameter</c> and never in <c>Base</c>.
    /// </summary>
    /// <remarks>
    /// Interface constraints count alongside base-type ones: <c>where T : IDisposable</c> is the
    /// same relationship, and the corpus carries four times as many of them, so reading only
    /// <c>BaseTypeName</c> would report the plausible absence this tool exists to avoid. Members
    /// carry type parameters too, so generic methods are read the same way generic types are.
    /// </remarks>
    private static IEnumerable<ApiReferenceHit> ReadConstraintHits(
        XElement declaration,
        string symbol,
        string owningSymbol,
        string? signature,
        SourceProvenance provenance)
    {
        foreach (var typeParameter in declaration.Element("TypeParameters")?.Elements("TypeParameter") ?? [])
        {
            foreach (var constraint in typeParameter.Element("Constraints")?.Elements() ?? [])
            {
                if (constraint.Name.LocalName is not ("BaseTypeName" or "InterfaceName"))
                    continue;

                if (ReferencesType(constraint.Value, symbol, out var isExact))
                {
                    yield return new ApiReferenceHit(
                        owningSymbol,
                        ApiReferenceKind.Constraint,

                        // The constrained type parameter, which is the only thing that says which
                        // of a declaration's constraints this hit came from.
                        typeParameter.Attribute("Name")?.Value,
                        constraint.Value,
                        isExact,
                        signature,
                        provenance);
                }
            }
        }
    }

    /// <summary>
    /// Attribute applications on one declaration — <c>[JsonConverter(typeof(SomeConverter))]</c>.
    /// </summary>
    /// <remarks>
    /// The C# rendering is the one read, since the F# sibling spells the same application
    /// differently and would double every hit. A repeated <c>FrameworkAlternate</c> variant that
    /// renders to identical text is one application recorded twice, so it is reported once; text
    /// that genuinely differs between variants survives.
    /// </remarks>
    private static IEnumerable<ApiReferenceHit> ReadAttributeHits(
        XElement declaration,
        string symbol,
        string owningSymbol,
        string? signature,
        SourceProvenance provenance)
    {
        var applications = (declaration.Element("Attributes")?.Elements("Attribute") ?? [])
            .Select(attribute => attribute.Elements("AttributeName")
                .FirstOrDefault(name => name.Attribute("Language") is not { } language
                    || string.Equals(language.Value, "C#", StringComparison.Ordinal))
                ?.Value)
            .Where(application => application is not null)
            .Distinct(StringComparer.Ordinal);

        foreach (var application in applications)
        {
            if (!ReferencesType(application!, symbol, out _))
                continue;

            // The application text carries the attribute's arguments, where a type is named as
            // readily as in the attribute's own name — [JsonConverter(typeof(X))] names two. So
            // exactness is decided against the attribute being applied, which is what separates
            // "decorated with this attribute" from "named inside its arguments".
            var isExact = ReferencesType(AttributeTypeName(application!), symbol, out var namesTheAttribute)
                && namesTheAttribute;
            yield return new ApiReferenceHit(
                owningSymbol,
                ApiReferenceKind.Attribute,
                null,
                application,
                isExact,
                signature,
                provenance);
        }
    }

    /// <summary>
    /// The attribute type an application names, out of the text ECMA XML records for it:
    /// <c>[get: System.Obsolete("…")]</c> names <c>System.Obsolete</c>.
    /// </summary>
    private static string AttributeTypeName(string application)
    {
        var text = application.AsSpan().Trim();
        if (text is ['[', .., ']'])
            text = text[1..^1];

        // Before the argument list, because a string argument can hold anything — including the
        // colon a target specifier is found by.
        var arguments = text.IndexOf('(');
        if (arguments >= 0)
            text = text[..arguments];

        var target = text.IndexOf(':');
        return text[(target + 1)..].Trim().ToString();
    }

    /// <summary>
    /// Whether a type expression uses <paramref name="symbol"/> as a whole type, and whether the
    /// expression is that type itself rather than one parameterized by it.
    /// </summary>
    /// <remarks>
    /// Equality is not enough and substring is too much. A parameter is far more often
    /// <c>System.String[]</c> or <c>System.String&amp;</c> or
    /// <c>IEnumerable&lt;System.String&gt;</c> than a bare <c>System.String</c>, so equality would
    /// miss every <c>params string[]</c> and every <c>out string</c> — absences that read as facts.
    /// A plain substring test would instead match <c>System.StringComparer</c>, so the occurrence
    /// has to sit on type-name boundaries.
    /// </remarks>
    private static bool ReferencesType(string typeExpression, string symbol, out bool isExact)
    {
        var start = 0;
        while (true)
        {
            var index = typeExpression.IndexOf(symbol, start, StringComparison.Ordinal);
            if (index < 0)
            {
                isExact = false;
                return false;
            }

            var before = index == 0 ? '\0' : typeExpression[index - 1];
            var afterIndex = index + symbol.Length;
            var after = afterIndex >= typeExpression.Length ? '\0' : typeExpression[afterIndex];

            if (!ContinuesName(before) && !ContinuesName(after))
            {
                isExact = index == 0 && afterIndex == typeExpression.Length;
                return true;
            }

            start = index + 1;
        }

        // A letter, digit or underscore on either side means a longer identifier; a dot means a
        // longer path, so "System.String" must not match inside "Foo.System.String" or
        // "System.String.Enumerator". '+' separates a nested type, which is a different type again.
        static bool ContinuesName(char character) =>
            char.IsLetterOrDigit(character) || character is '_' or '.' or '+';
    }

    private string[] ResolveSourceNames(string? source)
    {
        if (source is not null)
        {
            if (!ApiRootSegments.ContainsKey(source) || !_catalog.Sources.ContainsKey(source))
            {
                throw new ArgumentException(
                    "source must be \"dotnet-api-docs\" or \"roslyn-api-docs\".",
                    nameof(source));
            }

            return [source];
        }

        return ApiRootSegments.Keys
            .Where(_catalog.Sources.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static LookupRead ReadLookupSource(
        string sourceName,
        string directory,
        string symbol,
        SourceDefinition definition,
        SourceSyncState state)
    {
        var docsRoot = ResolveDocsRoot(sourceName, directory);
        var (files, memberName) = ResolveSymbol(docsRoot, symbol);
        var documented = files
            .Select(file => ReadType(file, memberName, definition, state))
            .ToArray();

        return new LookupRead(
            ToProvenance(definition, state),
            documented.Where(type => memberName is null || type.Members.Count > 0).ToArray(),

            // Every type whose name matched, before member filtering. This is what distinguishes
            // "no such type" from "the type exists and the member did not match".
            documented.Select(type => type.FullName).ToArray());
    }

    private sealed record LookupRead(
        SourceProvenance Provenance,
        IReadOnlyList<ApiTypeDocumentation> Matches,
        IReadOnlyList<string> ResolvedTypeNames);

    private static SourceRead<ApiSearchItem> ReadSearchSource(
        string sourceName,
        string directory,
        string pattern,
        SourceDefinition definition,
        SourceSyncState state,
        CancellationToken cancellationToken)
    {
        var docsRoot = ResolveDocsRoot(sourceName, directory);
        var provenance = ToProvenance(definition, state);
        var items = new List<ApiSearchItem>();
        foreach (var namespaceDirectory in Directory.EnumerateDirectories(docsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceName = Path.GetFileName(namespaceDirectory);
            var namespaceSegments = namespaceName.Split('.');
            var patternSegments = pattern.Split('.');
            foreach (var file in Directory.EnumerateFiles(namespaceDirectory, "*.xml"))
            {
                var typeName = Path.GetFileNameWithoutExtension(file);
                var (matchedOn, namespaceDepth) =
                    ClassifyMatch(namespaceSegments, typeName, pattern, patternSegments);
                if (matchedOn is not null)
                {
                    items.Add(new ApiSearchItem(
                        $"{namespaceName}.{typeName}", matchedOn, namespaceDepth, provenance));
                }
            }
        }

        return new SourceRead<ApiSearchItem>(provenance, items);
    }

    /// <summary>
    /// Decides which part of a fully-qualified name a pattern matched, or null for no match, and
    /// for a namespace match how far below the named namespace the type sits.
    /// </summary>
    /// <remarks>
    /// A caller cannot know which kind of string it is holding — a whole name copied out of a
    /// compiler error, a namespace, a fragment from the middle of one, or a bare type name — so all
    /// four have to work. The namespace side matches whole dot-separated segments rather than raw
    /// substrings: "Json" naming the <c>System.Text.Json</c> namespace is a question worth
    /// answering, while "Jso" naming it is far more likely to be a type-name fragment, and treating
    /// it as a namespace would bury the type the caller wanted under everything that namespace
    /// holds.
    /// </remarks>
    private static (string? MatchedOn, int? NamespaceDepth) ClassifyMatch(
        string[] namespaceSegments,
        string typeName,
        string pattern,
        string[] patternSegments)
    {
        // The type name is the last segment of the fully-qualified name, so a dotted pattern can
        // legitimately end on it.
        var fullNameSegments = new string[namespaceSegments.Length + 1];
        namespaceSegments.CopyTo(fullNameSegments, 0);
        fullNameSegments[^1] = typeName;

        var runStart = IndexOfSegmentRun(fullNameSegments, patternSegments);
        var runEnd = runStart + patternSegments.Length - 1;
        if (runStart >= 0)
        {
            // Only a multi-segment run reaching the type name is a whole-name match. A single
            // segment equal to the type name is just a type match spelled exactly.
            if (runEnd == fullNameSegments.Length - 1 && patternSegments.Length > 1)
                return (ApiNameMatch.FullName, null);
        }

        // Substring, not segment: "Concurrent" has to keep finding ConcurrentDictionary.
        if (typeName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            return (ApiNameMatch.Type, null);

        // A run ending one segment before the type name names the namespace the type is declared
        // in; one ending earlier names an ancestor of it. Reported rather than filtered, because
        // the descendant reading is what a caller naming a namespace means nearly every time, and
        // a page already in hand is where the narrowing costs nothing.
        return runStart >= 0
            ? (ApiNameMatch.Namespace, fullNameSegments.Length - 2 - runEnd)
            : (null, null);
    }

    /// <summary>
    /// Finds where <paramref name="run"/> occurs as consecutive whole segments of
    /// <paramref name="segments"/>, or -1.
    /// </summary>
    private static int IndexOfSegmentRun(string[] segments, string[] run)
    {
        if (run.Length == 0 || run.Length > segments.Length)
            return -1;

        // A pattern with an empty segment ("System..Text", or a trailing dot) names nothing.
        foreach (var segment in run)
        {
            if (segment.Length == 0)
                return -1;
        }

        for (var start = 0; start <= segments.Length - run.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < run.Length; offset++)
            {
                if (!string.Equals(segments[start + offset], run[offset], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return start;
        }

        return -1;
    }

    private static string ResolveDocsRoot(string sourceName, string directory)
    {
        var docsRoot = Path.Combine(directory, Path.Combine(ApiRootSegments[sourceName]));
        if (!Directory.Exists(docsRoot))
            throw new InvalidDataException($"{sourceName} is synced but its API docs root is missing: {docsRoot}");
        return docsRoot;
    }

    private static (string[] Files, string? MemberName) ResolveSymbol(
        string docsRoot,
        string symbol)
    {
        var typeFiles = FindTypeFiles(docsRoot, symbol);
        if (typeFiles.Length > 0)
            return (typeFiles, null);

        var separator = symbol.LastIndexOf('.');
        if (separator < 0)
            return ([], null);

        return (FindTypeFiles(docsRoot, symbol[..separator]), symbol[(separator + 1)..]);
    }

    private static string[] FindTypeFiles(string docsRoot, string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        if (separator >= 0)
        {
            var namespaceName = typeName[..separator];
            var simpleName = typeName[(separator + 1)..];
            return FindTypeFilesInNamespace(ResolveNamespaceDirectory(docsRoot, namespaceName), simpleName);
        }

        return Directory.EnumerateDirectories(docsRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(namespaceDirectory => FindTypeFilesInNamespace(namespaceDirectory, typeName))
            .ToArray();
    }

    private static string[] FindTypeFilesInNamespace(
        string namespaceDirectory,
        string simpleName)
    {
        if (!Directory.Exists(namespaceDirectory))
            return [];

        // Both, not one or the other. A namespace holding SyntaxList and SyntaxList`1 would
        // otherwise answer for the plain name with the smaller type and say nothing about the
        // larger one, which reads as a complete result.
        var files = new List<string>();
        var exact = Path.Combine(namespaceDirectory, simpleName + ".xml");
        if (File.Exists(exact))
            files.Add(exact);

        files.AddRange(Directory
            .EnumerateFiles(namespaceDirectory, simpleName + "`*.xml")
            .OrderBy(path => path, StringComparer.Ordinal));
        return files.ToArray();
    }

    /// <summary>
    /// ECMA XML spells a generic member's MemberName with its type-parameter list, so
    /// "Select&lt;TSource,TResult&gt;" is what an agent asking for "Select" must match. The fully
    /// specified form is still accepted, which is how a caller selects one arity.
    /// </summary>
    private static bool MemberNameMatches(string? attributeValue, string requested)
    {
        if (attributeValue is null)
            return false;
        if (string.Equals(attributeValue, requested, StringComparison.Ordinal))
            return true;

        var typeParameters = attributeValue.IndexOf('<', StringComparison.Ordinal);
        return typeParameters > 0
            && string.Equals(attributeValue[..typeParameters], requested, StringComparison.Ordinal);
    }

    private static ApiTypeDocumentation ReadType(
        string path,
        string? memberName,
        SourceDefinition definition,
        SourceSyncState state)
    {
        var root = XDocument.Load(path).Root
            ?? throw new InvalidDataException($"{path} has no XML root element.");
        var fullName = root.Attribute("FullName")?.Value
            ?? root.Attribute("Name")?.Value
            ?? Path.GetFileNameWithoutExtension(path);
        var members = root.Descendants("Member")
            .Where(member => memberName is null
                || MemberNameMatches(member.Attribute("MemberName")?.Value, memberName))
            .Select(ReadMember)
            .Where(member => member is not null)
            .Cast<ApiMemberDocumentation>()
            .ToArray();

        return new ApiTypeDocumentation(
            FullName: fullName,
            Members: members,
            Source: new SourceProvenance(
                Repo: definition.Repository,
                Ref: state.Ref,
                Commit: state.Commit,
                FetchedAt: state.FetchedAt),

            // A bare type name asks for an inventory; naming a member asks for its documentation.
            // The symbol is the whole selector, so the expensive response is unreachable rather
            // than merely opt-out. The tier is settled here because the reading is per source: one
            // source resolving the string as a type must not collapse another source's member
            // match, for which full documentation was the right answer.
            Detail: memberName is null ? ApiLookupDetail.Signatures : ApiLookupDetail.Full);
    }

    private static ApiMemberDocumentation? ReadMember(XElement member)
    {
        var signature = member.Elements("MemberSignature")
            .LastOrDefault(element => string.Equals(
                element.Attribute("Language")?.Value,
                "C#",
                StringComparison.Ordinal))
            ?.Attribute("Value")?.Value;
        if (signature is null)
            return null;

        var docs = member.Element("Docs");
        var parameters = member.Element("Parameters")?.Elements("Parameter")
            .Select(parameter =>
            {
                var name = parameter.Attribute("Name")?.Value ?? string.Empty;
                var description = docs?.Elements("param")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("name")?.Value,
                        name,
                        StringComparison.Ordinal));
                return new ApiParameterDocumentation(name, RenderDocumentation(description));
            })
            .ToArray()
            ?? [];

        return new ApiMemberDocumentation(
            Name: member.Attribute("MemberName")?.Value ?? string.Empty,
            Signature: signature,
            Summary: RenderDocumentation(docs?.Element("summary")),
            Parameters: parameters,
            Returns: RenderDocumentation(docs?.Element("returns")),
            Remarks: RenderDocumentation(docs?.Element("remarks")));
    }

    /// <summary>
    /// Flattens a documentation element to text, resolving the reference elements to the names they
    /// name. <see cref="XElement.Value"/> alone concatenates text nodes and discards child elements,
    /// and ECMA XML carries every type and parameter reference as an empty element — so it turns
    /// "converts the value into a &lt;see cref="T:System.String" /&gt;." into "converts the value
    /// into a .", a sentence that still reads as complete while missing the thing it names.
    /// The result is normalized before it is returned, so no caller can match against text that
    /// differs from the text it will later hand back.
    /// </summary>
    private static string? RenderDocumentation(XElement? element)
    {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        AppendNodes(element, builder);

        // A <format> child means the body is markdown, where the line structure is content: folding
        // it would run fenced code blocks and lists into one line.
        var isMarkdown = element.Descendants("format").Any();
        return DocumentationText.Normalize(builder.ToString(), collapseWhitespace: !isMarkdown);

        static void AppendNodes(XElement parent, StringBuilder builder)
        {
            foreach (var node in parent.Nodes())
            {
                switch (node)
                {
                    // XCData derives from XText, which is what carries the markdown <format> blocks.
                    case XText text:
                        builder.Append(text.Value);
                        break;
                    case XElement child:
                        AppendElement(child, builder);
                        break;
                }
            }
        }

        static void AppendElement(XElement element, StringBuilder builder)
        {
            switch (element.Name.LocalName)
            {
                case "see":
                case "seealso":
                    // A cref is kept whole, prefix stripped: "T:System.String" reads as
                    // "System.String", which is also exactly what lookup_api accepts back.
                    var reference = StripDocumentationIdPrefix(element.Attribute("cref")?.Value)
                        ?? element.Attribute("langword")?.Value
                        ?? element.Attribute("href")?.Value;
                    if (reference is not null && element.IsEmpty)
                    {
                        builder.Append(reference);
                        return;
                    }

                    if (element.IsEmpty)
                        return;
                    break;

                case "paramref":
                case "typeparamref":
                    var name = element.Attribute("name")?.Value;
                    if (name is not null)
                    {
                        builder.Append(name);
                        return;
                    }

                    break;
            }

            AppendNodes(element, builder);
        }
    }

    /// <summary>
    /// Strips the single-letter documentation-ID prefix ECMA XML puts on every cref — "T:" for a
    /// type, "M:" for a method, and so on.
    /// </summary>
    private static string? StripDocumentationIdPrefix(string? cref) =>
        cref is { Length: > 2 } && cref[1] == ':' && char.IsAsciiLetter(cref[0])
            ? cref[2..]
            : cref;

    private static void ValidateSymbol(string symbol)
    {
        if (Path.IsPathRooted(symbol)
            || symbol.IndexOfAny(['/', '\\', ':', '*', '?', '[', ']']) >= 0
            || symbol.Split('.').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new ArgumentException("symbol contains invalid path or wildcard characters.", nameof(symbol));
        }
    }

    private static string ResolveNamespaceDirectory(string docsRoot, string namespaceName)
    {
        var fullRoot = Path.GetFullPath(docsRoot);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, namespaceName));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
            throw new ArgumentException("symbol resolves outside the API documentation root.", nameof(namespaceName));

        return candidate;
    }

    private static SourceProvenance ToProvenance(SourceDefinition definition, SourceSyncState state) =>
        new(definition.Repository, state.Ref, state.Commit, state.FetchedAt);

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

    private sealed record SourceRead<T>(SourceProvenance Provenance, IReadOnlyList<T> Items);
}
