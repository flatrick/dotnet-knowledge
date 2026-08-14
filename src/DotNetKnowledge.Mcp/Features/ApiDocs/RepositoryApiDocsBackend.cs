using System.Xml.Linq;
using DotNetKnowledge.Mcp.Sources;
using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

internal sealed class RepositoryApiDocsBackend : IApiDocsBackend
{
    private const int MatchTextBudget = 300;

    private static readonly IReadOnlyDictionary<string, string[]> ApiRootSegments =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["dotnet-api-docs"] = ["xml"],
            ["roslyn-api-docs"] = ["dotnet", "xml"],
        };

    private readonly string _docsRoot;
    private readonly GitProvenance _provenance;
    private readonly ApiQueryCoverage _coverage;

    public RepositoryApiDocsBackend(string sourceName, SourceSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(snapshot);
        _docsRoot = ResolveDocsRoot(sourceName, snapshot.RepositoryDirectory);
        _provenance = ToProvenance(snapshot.Definition, snapshot.State);
        _coverage = new ApiQueryCoverage([_provenance], null, null, null);
    }

    internal static IEnumerable<string> SourceNames => ApiRootSegments.Keys;

    internal static bool Supports(string sourceName) => ApiRootSegments.ContainsKey(sourceName);

    public ApiLookupRead Lookup(string symbol, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (files, memberName) = ResolveSymbol(_docsRoot, symbol);
        var documented = files
            .Select(file => ReadType(file, memberName, _provenance))
            .ToArray();
        RejectDuplicateDeclarationIds(
            documented.Select(type => type.DeclarationId), "type");
        return new ApiLookupRead(
            documented.Where(type => memberName is null || type.Members.Count > 0).ToArray(),
            documented.Select(type => type.FullName).ToArray(),
            _coverage);
    }

    public ApiSearchRead Search(string pattern, CancellationToken cancellationToken)
    {
        var items = new List<ApiSearchItem>();
        foreach (var namespaceDirectory in Directory.EnumerateDirectories(_docsRoot))
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
                        $"{namespaceName}.{typeName}", matchedOn, namespaceDepth, _provenance));
                }
            }
        }

        return new ApiSearchRead(items, _coverage);
    }

    public ApiTextRead SearchText(string query, CancellationToken cancellationToken)
    {
        var prefilter = LongestToken(query);
        var files = Directory.EnumerateFiles(_docsRoot, "*.xml", SearchOption.AllDirectories);
        var hits = new System.Collections.Concurrent.ConcurrentBag<ApiTextHitRead>();
        var declarations = new System.Collections.Concurrent.ConcurrentDictionary<string, object>(
            StringComparer.Ordinal);
        try
        {
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
                    foreach (var hit in ReadTextHits(file, text, query, _provenance, declarations))
                        hits.Add(hit);
                });
        }
        catch (AggregateException exception) when (
            exception.Flatten().InnerExceptions.All(item => item is InvalidDataException))
        {
            throw new InvalidDataException(
                "Repository declarations contain duplicate canonical IDs.", exception);
        }
        return new ApiTextRead(hits.ToArray(), _coverage);
    }

    public ApiReferenceRead FindReferences(string symbol, CancellationToken cancellationToken)
    {
        var attributes = new AttributeResolution(_docsRoot, symbol);
        var shortForm = AttributeResolution.ShortForm(symbol);
        var files = Directory.EnumerateFiles(_docsRoot, "*.xml", SearchOption.AllDirectories);
        var hits = new System.Collections.Concurrent.ConcurrentBag<ApiReferenceHitRead>();
        var declarations = new System.Collections.Concurrent.ConcurrentDictionary<string, object>(
            StringComparer.Ordinal);
        try
        {
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

                    if (!text.Contains(symbol, StringComparison.Ordinal)
                        && (shortForm is null || !text.Contains(shortForm, StringComparison.Ordinal)))
                    {
                        return;
                    }

                    foreach (var hit in ReadReferenceHits(
                                 file, text, attributes, _provenance, declarations))
                        hits.Add(hit);
                });
        }
        catch (AggregateException exception) when (
            exception.Flatten().InnerExceptions.All(item => item is InvalidDataException))
        {
            throw new InvalidDataException(
                "Repository declarations contain duplicate canonical IDs.", exception);
        }
        return new ApiReferenceRead(
            hits.ToArray(), attributes.SiblingType, attributes.SiblingApplications, _coverage);
    }

    private static string LongestToken(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? query : tokens.MaxBy(token => token.Length)!;
    }

    private static IEnumerable<ApiTextHitRead> ReadTextHits(
        string path,
        string text,
        string query,
        ApiProvenance provenance,
        System.Collections.Concurrent.ConcurrentDictionary<string, object> declarations)
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
        if (root.Name.LocalName is not ("Type" or "Namespace"))
            yield break;
        var fullName = RequiredDeclarationName(root, path);
        var typeId = ReadRootDeclarationId(root, fullName);
        RegisterDeclaration(declarations, typeId, root);
        foreach (var hit in MatchDocs(root.Element("Docs"), typeId, fullName, query, provenance))
            yield return hit;
        foreach (var member in root.Descendants("Member"))
        {
            var memberName = member.Attribute("MemberName")?.Value;
            var symbol = memberName is null ? fullName : $"{fullName}.{memberName}";
            var declarationId = ReadMemberDeclarationId(member, typeId);
            RegisterDeclaration(declarations, declarationId, member);
            foreach (var hit in MatchDocs(member.Element("Docs"), declarationId, symbol, query, provenance))
                yield return hit;
        }
    }

    private static IEnumerable<ApiTextHitRead> MatchDocs(
        XElement? docs,
        string declarationId,
        string symbol,
        string query,
        ApiProvenance provenance)
    {
        if (docs is null)
            yield break;
        foreach (var element in docs.Elements())
        {
            var name = element.Name.LocalName;
            if (name is not ("summary" or "remarks" or "returns" or "param" or "typeparam" or "value" or "exception"))
                continue;
            var rendered = DocumentationTextRenderer.Render(element);
            if (rendered is null || !rendered.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var label = element.Attribute("name")?.Value is { Length: > 0 } parameterName
                ? $"{name}:{parameterName}"
                : name;
            var (text, isTruncated) = DocumentationText.Budget(rendered, MatchTextBudget);
            yield return new ApiTextHitRead(
                declarationId,
                new ApiTextHit(symbol, label, text, isTruncated, provenance));
        }
    }

    private static IEnumerable<ApiReferenceHitRead> ReadReferenceHits(
        string path,
        string text,
        AttributeResolution attributes,
        ApiProvenance provenance,
        System.Collections.Concurrent.ConcurrentDictionary<string, object> declarations)
    {
        var symbol = attributes.Symbol;
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
        if (root.Name.LocalName is not ("Type" or "Namespace"))
            yield break;
        var fullName = RequiredDeclarationName(root, path);
        var typeId = ReadRootDeclarationId(root, fullName);
        RegisterDeclaration(declarations, typeId, root);
        if (string.Equals(fullName, symbol, StringComparison.Ordinal))
            yield break;

        var baseTypeName = root.Element("Base")?.Element("BaseTypeName")?.Value;
        if (baseTypeName is not null && ReferencesType(baseTypeName, symbol, out var baseIsExact))
            yield return Wrap(typeId, new ApiReferenceHit(fullName, ApiReferenceKind.Base, null, baseTypeName, null, baseIsExact, null, provenance));
        foreach (var interfaceName in root.Element("Interfaces")?.Elements("Interface")
                     .Select(item => item.Element("InterfaceName")?.Value) ?? [])
        {
            if (interfaceName is not null && ReferencesType(interfaceName, symbol, out var interfaceIsExact))
                yield return Wrap(typeId, new ApiReferenceHit(fullName, ApiReferenceKind.Interface, null, interfaceName, null, interfaceIsExact, null, provenance));
        }
        foreach (var hit in ReadConstraintHits(root, symbol, typeId, fullName, null, provenance))
            yield return hit;
        foreach (var hit in ReadAttributeHits(root, attributes, typeId, fullName, null, provenance))
            yield return hit;

        foreach (var member in root.Descendants("Member"))
        {
            var memberName = member.Attribute("MemberName")?.Value;
            var memberSymbol = memberName is null ? fullName : $"{fullName}.{memberName}";
            var memberId = ReadMemberDeclarationId(member, typeId);
            RegisterDeclaration(declarations, memberId, member);
            var signature = member.Elements("MemberSignature")
                .LastOrDefault(element => string.Equals(element.Attribute("Language")?.Value, "C#", StringComparison.Ordinal))
                ?.Attribute("Value")?.Value;
            foreach (var hit in ReadConstraintHits(member, symbol, memberId, memberSymbol, signature, provenance))
                yield return hit;
            foreach (var hit in ReadAttributeHits(member, attributes, memberId, memberSymbol, signature, provenance))
                yield return hit;
            var returnType = member.Element("ReturnValue")?.Element("ReturnType")?.Value;
            if (returnType is not null && ReferencesType(returnType, symbol, out var returnIsExact))
                yield return Wrap(memberId, new ApiReferenceHit(memberSymbol, ApiReferenceKind.Return, null, returnType, null, returnIsExact, signature, provenance));
            foreach (var parameter in member.Element("Parameters")?.Elements("Parameter") ?? [])
            {
                var parameterType = parameter.Attribute("Type")?.Value;
                if (parameterType is not null && ReferencesType(parameterType, symbol, out var parameterIsExact))
                {
                    yield return Wrap(memberId, new ApiReferenceHit(
                        memberSymbol, ApiReferenceKind.Parameter, parameter.Attribute("Name")?.Value,
                        parameterType, null, parameterIsExact, signature, provenance));
                }
            }
        }
    }

    private static IEnumerable<ApiReferenceHitRead> ReadConstraintHits(
        XElement declaration,
        string symbol,
        string declarationId,
        string owningSymbol,
        string? signature,
        ApiProvenance provenance)
    {
        foreach (var typeParameter in declaration.Element("TypeParameters")?.Elements("TypeParameter") ?? [])
        {
            foreach (var constraint in typeParameter.Element("Constraints")?.Elements() ?? [])
            {
                if (constraint.Name.LocalName is not ("BaseTypeName" or "InterfaceName"))
                    continue;
                if (ReferencesType(constraint.Value, symbol, out var isExact))
                {
                    yield return Wrap(declarationId, new ApiReferenceHit(
                        owningSymbol, ApiReferenceKind.Constraint, typeParameter.Attribute("Name")?.Value,
                        constraint.Value, null, isExact, signature, provenance));
                }
            }
        }
    }

    private static IEnumerable<ApiReferenceHitRead> ReadAttributeHits(
        XElement declaration,
        AttributeResolution attributes,
        string declarationId,
        string owningSymbol,
        string? signature,
        ApiProvenance provenance)
    {
        var symbol = attributes.Symbol;
        var applications = (declaration.Element("Attributes")?.Elements("Attribute") ?? [])
            .Select(attribute => attribute.Elements("AttributeName")
                .FirstOrDefault(name => name.Attribute("Language") is not { } language
                    || string.Equals(language.Value, "C#", StringComparison.Ordinal))
                ?.Value)
            .Where(application => application is not null)
            .Distinct(StringComparer.Ordinal);
        foreach (var application in applications)
        {
            var appliedName = AttributeTypeName(application!);
            var attributeType = attributes.Resolve(appliedName);
            var namesTheAttribute = string.Equals(attributeType, symbol, StringComparison.Ordinal);
            if (!namesTheAttribute && string.Equals(appliedName, symbol, StringComparison.Ordinal))
            {
                attributes.CountSiblingApplication();
                continue;
            }
            if (!namesTheAttribute && !ReferencesType(application!, symbol, out _))
                continue;
            yield return Wrap(declarationId, new ApiReferenceHit(
                owningSymbol, ApiReferenceKind.Attribute, null, application, attributeType,
                namesTheAttribute, signature, provenance));
        }
    }

    private sealed class AttributeResolution
    {
        private const string Suffix = "Attribute";
        private readonly string _docsRoot;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _resolved =
            new(StringComparer.Ordinal);
        private int _siblingApplications;

        public AttributeResolution(string docsRoot, string symbol)
        {
            _docsRoot = docsRoot;
            Symbol = symbol;
            var candidate = symbol + Suffix;
            SiblingType = TypeExists(candidate) ? candidate : null;
        }

        public string Symbol { get; }
        public string? SiblingType { get; }
        public int SiblingApplications => Volatile.Read(ref _siblingApplications);

        public static string? ShortForm(string symbol)
        {
            if (!symbol.EndsWith(Suffix, StringComparison.Ordinal))
                return null;
            var simpleName = symbol.LastIndexOf('.') + 1;
            return symbol.Length - simpleName > Suffix.Length ? symbol[..^Suffix.Length] : null;
        }

        public string Resolve(string appliedName) => _resolved.GetOrAdd(
            appliedName, name => TypeExists(name + Suffix) ? name + Suffix : name);
        public void CountSiblingApplication() => Interlocked.Increment(ref _siblingApplications);

        private bool TypeExists(string typeName)
        {
            try
            {
                return FindTypeFiles(_docsRoot, typeName).Length > 0;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    private static string AttributeTypeName(string application)
    {
        var text = application.AsSpan().Trim();
        if (text is ['[', .., ']'])
            text = text[1..^1];
        var arguments = text.IndexOf('(');
        if (arguments >= 0)
            text = text[..arguments];
        var target = text.IndexOf(':');
        return text[(target + 1)..].Trim().ToString();
    }

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

        static bool ContinuesName(char character) =>
            char.IsLetterOrDigit(character) || character is '_' or '.' or '+';
    }

    private static (string? MatchedOn, int? NamespaceDepth) ClassifyMatch(
        string[] namespaceSegments,
        string typeName,
        string pattern,
        string[] patternSegments)
    {
        var fullNameSegments = new string[namespaceSegments.Length + 1];
        namespaceSegments.CopyTo(fullNameSegments, 0);
        fullNameSegments[^1] = typeName;
        var runStart = IndexOfSegmentRun(fullNameSegments, patternSegments);
        var runEnd = runStart + patternSegments.Length - 1;
        if (runStart >= 0 && runEnd == fullNameSegments.Length - 1 && patternSegments.Length > 1)
            return (ApiNameMatch.FullName, null);
        if (typeName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            return (ApiNameMatch.Type, null);
        return runStart >= 0
            ? (ApiNameMatch.Namespace, fullNameSegments.Length - 2 - runEnd)
            : (null, null);
    }

    private static int IndexOfSegmentRun(string[] segments, string[] run)
    {
        if (run.Length == 0 || run.Length > segments.Length)
            return -1;
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
        if (!ApiRootSegments.TryGetValue(sourceName, out var segments))
            throw new ArgumentException("source is not an API documentation repository.", nameof(sourceName));
        var docsRoot = Path.Combine(directory, Path.Combine(segments));
        if (!Directory.Exists(docsRoot))
            throw new InvalidDataException($"{sourceName} is synced but its API docs root is missing: {docsRoot}");
        return docsRoot;
    }

    private static (string[] Files, string? MemberName) ResolveSymbol(string docsRoot, string symbol)
    {
        var typeFiles = FindTypeFiles(docsRoot, symbol);
        if (typeFiles.Length > 0)
            return (typeFiles, null);
        var separator = symbol.LastIndexOf('.');
        return separator < 0
            ? ([], null)
            : (FindTypeFiles(docsRoot, symbol[..separator]), symbol[(separator + 1)..]);
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

    private static string[] FindTypeFilesInNamespace(string namespaceDirectory, string simpleName)
    {
        if (!Directory.Exists(namespaceDirectory))
            return [];
        var files = new List<string>();
        var exact = Path.Combine(namespaceDirectory, simpleName + ".xml");
        if (File.Exists(exact))
            files.Add(exact);
        files.AddRange(Directory.EnumerateFiles(namespaceDirectory, simpleName + "`*.xml")
            .OrderBy(path => path, StringComparer.Ordinal));
        return files.ToArray();
    }

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

    private static ApiLookupTypeRead ReadType(string path, string? memberName, GitProvenance provenance)
    {
        var root = XDocument.Load(path).Root
            ?? throw new InvalidDataException($"{path} has no XML root element.");
        var fullName = RequiredDeclarationName(root, path);
        var typeId = ReadRootDeclarationId(root, fullName);
        var members = root.Descendants("Member")
            .Where(member => memberName is null || MemberNameMatches(member.Attribute("MemberName")?.Value, memberName))
            .Select(member => ReadMember(member, typeId))
            .Where(member => member is not null)
            .Cast<ApiLookupMemberRead>()
            .ToArray();
        RejectDuplicateDeclarationIds(
            members.Select(member => member.DeclarationId), "member");
        var documentation = new ApiTypeDocumentation(
            fullName,
            members.Select(member => member.Documentation).ToArray(),
            provenance,
            memberName is null ? ApiLookupDetail.Signatures : ApiLookupDetail.Full);
        return new ApiLookupTypeRead(
            typeId,
            documentation,
            members);
    }

    private static ApiLookupMemberRead? ReadMember(XElement member, string typeDeclarationId)
    {
        var signature = member.Elements("MemberSignature")
            .LastOrDefault(element => string.Equals(element.Attribute("Language")?.Value, "C#", StringComparison.Ordinal))
            ?.Attribute("Value")?.Value;
        if (signature is null)
            return null;
        var docs = member.Element("Docs");
        var parameters = member.Element("Parameters")?.Elements("Parameter")
            .Select(parameter =>
            {
                var name = parameter.Attribute("Name")?.Value ?? string.Empty;
                var description = docs?.Elements("param").FirstOrDefault(element =>
                    string.Equals(element.Attribute("name")?.Value, name, StringComparison.Ordinal));
                return new ApiParameterDocumentation(name, DocumentationTextRenderer.Render(description));
            }).ToArray() ?? [];
        var documentation = new ApiMemberDocumentation(
            member.Attribute("MemberName")?.Value ?? string.Empty,
            signature,
            DocumentationTextRenderer.Render(docs?.Element("summary")),
            parameters,
            DocumentationTextRenderer.Render(docs?.Element("returns")),
            DocumentationTextRenderer.Render(docs?.Element("remarks")));
        return new ApiLookupMemberRead(
            ReadMemberDeclarationId(member, typeDeclarationId),
            documentation);
    }

    private static string ReadMemberDeclarationId(
        XElement member,
        string typeDeclarationId)
    {
        var signatures = member.Elements("MemberSignature")
            .Where(element => string.Equals(
                element.Attribute("Language")?.Value, "DocId", StringComparison.Ordinal))
            .Select(element => element.Attribute("Value")?.Value)
            .ToArray();
        if (signatures.Length == 0
            || signatures.Any(signature => !ApiDeclarationId.IsCanonicalMemberId(signature, typeDeclarationId)))
        {
            throw new InvalidDataException(
                $"Repository member '{member.Attribute("MemberName")?.Value}' has no valid canonical DocId.");
        }
        if (member.Attribute("MemberName")?.Value is
            "op_Implicit" or "op_Explicit" or "op_CheckedImplicit" or "op_CheckedExplicit")
        {
            var returnQualified = signatures
                .Where(signature => signature!.Contains('~', StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (returnQualified.Length == 1)
                return returnQualified[0]!;
            if (returnQualified.Length > 1)
            {
                throw new InvalidDataException(
                    $"Repository conversion member '{member.Attribute("MemberName")?.Value}' has ambiguous canonical DocIds.");
            }
        }
        var distinct = signatures.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length != 1)
        {
            throw new InvalidDataException(
                $"Repository member '{member.Attribute("MemberName")?.Value}' has ambiguous canonical DocIds.");
        }
        return distinct[0]!;
    }

    private static string ReadRootDeclarationId(XElement declaration, string name)
    {
        if (declaration.Name.LocalName == "Namespace")
        {
            if (!ApiDeclarationId.IsCanonicalNamespaceName(name))
                throw new InvalidDataException("Repository namespace has no valid canonical name.");
            return "N:" + name;
        }
        if (declaration.Name.LocalName != "Type")
            throw new InvalidDataException($"Unsupported repository declaration root '{declaration.Name}'.");
        var signatures = declaration.Elements("TypeSignature")
            .Where(element => string.Equals(
                element.Attribute("Language")?.Value, "DocId", StringComparison.Ordinal))
            .Select(element => element.Attribute("Value")?.Value)
            .ToArray();
        if (signatures.Length != 1 || !ApiDeclarationId.IsCanonicalTypeId(signatures[0]))
            throw new InvalidDataException($"Repository type '{name}' has no unique canonical T: DocId.");
        return signatures[0]!;
    }

    private static string RequiredDeclarationName(XElement root, string path)
    {
        var name = root.Attribute("FullName")?.Value ?? root.Attribute("Name")?.Value;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"Repository declaration '{path}' has no canonical name.");
        return name;
    }

    private static void RejectDuplicateDeclarationIds(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                throw new InvalidDataException($"Repository {kind} declaration ID '{id}' is duplicated.");
        }
    }

    private static void RegisterDeclaration(
        System.Collections.Concurrent.ConcurrentDictionary<string, object> declarations,
        string declarationId,
        object declaration)
    {
        if (!declarations.TryAdd(declarationId, declaration)
            && (!declarations.TryGetValue(declarationId, out var existing)
                || !ReferenceEquals(existing, declaration)))
        {
            throw new InvalidDataException(
                $"Repository declaration ID '{declarationId}' is duplicated.");
        }
    }

    private static ApiReferenceHitRead Wrap(string declarationId, ApiReferenceHit hit) =>
        new(declarationId, hit);

    private static string ResolveNamespaceDirectory(string docsRoot, string namespaceName)
    {
        var fullRoot = Path.GetFullPath(docsRoot);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, namespaceName));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPrefix, comparison))
            throw new ArgumentException("symbol resolves outside the API documentation root.", nameof(namespaceName));
        return candidate;
    }

    private static GitProvenance ToProvenance(SourceDefinition definition, SourceSyncState state) =>
        new(definition.Repository, state.Ref, state.Commit, state.FetchedAt);
}
