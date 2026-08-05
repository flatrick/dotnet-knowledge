using System.Xml.Linq;
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

public sealed class ApiDocsQueryService
{
    private static readonly IReadOnlyDictionary<string, string[]> ApiRootSegments =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dotnet-api-docs"] = ["xml"],
            ["roslyn-api-docs"] = ["dotnet", "xml"],
        };

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
        var reads = new List<LookupRead>();

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
            reads.Add(read);
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

        // A bare type name asks for an inventory; naming a member asks for its documentation. The
        // symbol is the whole selector, so the expensive response is unreachable rather than
        // merely opt-out. The reads already answered this question when they resolved the symbol,
        // so it is carried out rather than recomputed — recomputing risks the two disagreeing.
        var signaturesOnly = reads.Any(read => read.Matches.Count > 0 && !read.SymbolNamedAMember);

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
                    .Select(pair => signaturesOnly ? ToSignature(pair.Member!) : pair.Member!)
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
            documented.Select(type => type.FullName).ToArray(),
            memberName is not null);
    }

    private sealed record LookupRead(
        SourceProvenance Provenance,
        IReadOnlyList<ApiTypeDocumentation> Matches,
        IReadOnlyList<string> ResolvedTypeNames,
        bool SymbolNamedAMember);

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
            foreach (var file in Directory.EnumerateFiles(namespaceDirectory, "*.xml"))
            {
                var typeName = Path.GetFileNameWithoutExtension(file);
                if (typeName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    items.Add(new ApiSearchItem($"{namespaceName}.{typeName}", provenance));
            }
        }

        return new SourceRead<ApiSearchItem>(provenance, items);
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
                FetchedAt: state.FetchedAt));
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
                return new ApiParameterDocumentation(name, CleanDocumentation(RenderDocumentation(description)));
            })
            .ToArray()
            ?? [];

        return new ApiMemberDocumentation(
            Name: member.Attribute("MemberName")?.Value ?? string.Empty,
            Signature: signature,
            Summary: CleanDocumentation(RenderDocumentation(docs?.Element("summary"))),
            Parameters: parameters,
            Returns: CleanDocumentation(RenderDocumentation(docs?.Element("returns"))),
            Remarks: CleanDocumentation(RenderDocumentation(docs?.Element("remarks"))));
    }

    /// <summary>
    /// Flattens a documentation element to text, resolving the reference elements to the names they
    /// name. <see cref="XElement.Value"/> alone concatenates text nodes and discards child elements,
    /// and ECMA XML carries every type and parameter reference as an empty element — so it turns
    /// "converts the value into a &lt;see cref="T:System.String" /&gt;." into "converts the value
    /// into a .", a sentence that still reads as complete while missing the thing it names.
    /// </summary>
    private static string? RenderDocumentation(XElement? element)
    {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        AppendNodes(element, builder);
        return builder.ToString();

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

    private static string? CleanDocumentation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || string.Equals(text.Trim(), "To be added.", StringComparison.Ordinal))
        {
            return null;
        }

        return text.Trim();
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

    private static string EncodeCursor(string kind, string scope, int offset, IReadOnlyList<string> revisions)
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
