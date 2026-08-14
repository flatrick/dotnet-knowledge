using System.Text;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;
using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

internal sealed class PackageApiDocsBackend : IApiDocsBackend
{
    private const int MatchTextBudget = 300;
    private const int MaximumReportedSkipReasons = 10;

    private static readonly Dictionary<string, string> CSharpAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["System.SByte"] = "sbyte",
            ["System.Char"] = "char",
            ["System.Decimal"] = "decimal",
            ["System.Double"] = "double",
            ["System.Single"] = "float",
            ["System.Int32"] = "int",
            ["System.UInt32"] = "uint",
            ["System.Int64"] = "long",
            ["System.UInt64"] = "ulong",
            ["System.Int16"] = "short",
            ["System.UInt16"] = "ushort",
            ["System.Object"] = "object",
            ["System.String"] = "string",
            ["System.Void"] = "void",
            ["System.IntPtr"] = "nint",
            ["System.UIntPtr"] = "nuint",
        };

    private readonly ApiCorpus _corpus;
    private readonly NuGetProvenance _provenance;
    private readonly ApiQueryCoverage _coverage;

    public PackageApiDocsBackend(
        SourceSnapshot snapshot,
        string packageId,
        string framework)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        ValidateSnapshotIdentity(snapshot);
        var definition = JoinDefinition(snapshot, packageId);
        var state = JoinState(snapshot, packageId);
        ValidatePackageIdentity(snapshot, definition, state);
        if (!state.AvailableFrameworks.Contains(framework, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Package '{state.PackageId}' does not contain synchronized framework '{framework}'.");
        }

        var corpusDirectory = ResolveContainedDirectory(
            snapshot.SupplementsDirectory, state.CorpusDirectory);
        var corpusPath = ResolveCorpusPath(corpusDirectory, framework);
        var synchronizedDefinition = string.Equals(snapshot.State.Ref, "pinned", StringComparison.Ordinal)
            ? definition
            : definition.WithSynchronizedContent(state.Version, state.Sha512);
        _corpus = PackageApiCorpusStore.Read(corpusPath, synchronizedDefinition, framework);
        _provenance = new NuGetProvenance(
            state.PackageId,
            state.Version,
            state.Sha512,
            state.Feed,
            framework,
            state.FetchedAt);
        _coverage = new ApiQueryCoverage(
            [_provenance],
            framework,
            state.DefaultFramework,
            state.AvailableFrameworks.ToArray(),
            SummarizeSkipped(_corpus.Skipped));
    }

    /// <summary>
    /// Turns the corpus's skipped declarations into a payload-sized report: a total, and the most
    /// common reasons. The full list stays in the stored corpus — a package with hundreds of skips
    /// must not spend an agent's context enumerating them, and the total is what tells the agent
    /// its answer is drawn from an incomplete corpus.
    /// </summary>
    internal static ApiSkipCoverage? SummarizeSkipped(IReadOnlyList<ApiSkippedDeclaration> skipped)
    {
        if (skipped is null || skipped.Count == 0)
            return null;

        var grouped = skipped
            .GroupBy(item => CollapseQuoted(item.Reason), StringComparer.Ordinal)
            .Select(group => new ApiSkipReason(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Reason, StringComparer.Ordinal)
            .ToArray();

        return new ApiSkipCoverage(
            skipped.Count,
            grouped.Take(MaximumReportedSkipReasons).ToArray(),
            grouped.Length > MaximumReportedSkipReasons);
    }

    /// <summary>
    /// Replaces the quoted declaration name in a reader message with a placeholder. Without it the
    /// same defect reports once per member -- the names are interpolated into the message -- and the
    /// summary degenerates into the list it exists to replace.
    /// </summary>
    private static string CollapseQuoted(string message)
    {
        var result = new StringBuilder(message.Length);
        var inQuote = false;
        foreach (var character in message)
        {
            if (character == '\'')
            {
                result.Append(inQuote ? ">'" : "'<");
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote)
                result.Append(character);
        }

        return result.ToString();
    }

    public ApiLookupRead Lookup(string symbol, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var types = FindTypes(symbol);
        string? memberName = null;
        if (types.Length == 0)
        {
            var separator = symbol.LastIndexOf('.');
            if (separator >= 0)
            {
                types = FindTypes(symbol[..separator]);
                memberName = symbol[(separator + 1)..];
            }
        }

        var documented = types
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ThenBy(type => type.EcmaId, StringComparer.Ordinal)
            .Select(type => ReadType(type, memberName))
            .ToArray();
        return new ApiLookupRead(
            documented.Where(type => memberName is null || type.Members.Count > 0).ToArray(),
            documented,
            documented.Select(type => type.FullName).ToArray(),
            _coverage);
    }

    public ApiSearchRead Search(string pattern, CancellationToken cancellationToken)
    {
        var items = new List<ApiSearchItem>();
        var patternSegments = pattern.Split('.');
        foreach (var type in OrderedTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalName = CanonicalEcmaName(type.EcmaId);
            var namespaceName = FindNamespace(type);
            var namespaceSegments = namespaceName.Length == 0 ? [] : namespaceName.Split('.');
            var typeName = namespaceName.Length == 0
                ? canonicalName
                : canonicalName[(namespaceName.Length + 1)..];
            var (matchedOn, namespaceDepth) =
                ClassifyMatch(namespaceSegments, typeName, pattern, patternSegments);
            if (matchedOn is not null)
                items.Add(new ApiSearchItem(canonicalName, matchedOn, namespaceDepth, _provenance));
        }
        return new ApiSearchRead(items, _coverage);
    }

    public ApiTextRead SearchText(string query, CancellationToken cancellationToken)
    {
        var hits = new List<ApiTextHitRead>();
        foreach (var type in OrderedTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddDocumentationHits(hits, type.EcmaId, type.FullName, type.Documentation, query);
            foreach (var member in type.Members
                         .OrderBy(item => item.Name, StringComparer.Ordinal)
                         .ThenBy(item => item.Signature, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddDocumentationHits(
                    hits, member.EcmaId, $"{type.FullName}.{member.Name}", member.Documentation, query);
            }
        }
        return new ApiTextRead(hits, _coverage);
    }

    public ApiReferenceRead FindReferences(string symbol, CancellationToken cancellationToken)
    {
        var hits = new List<ApiReferenceHitRead>();
        var siblingType = TypeExists(symbol + "Attribute") ? symbol + "Attribute" : null;
        var siblingApplications = 0;
        foreach (var type in OrderedTypes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(type.FullName, symbol, StringComparison.Ordinal))
                continue;

            if (type.BaseType is not null
                && ReferencesTypeUse(type.BaseType, symbol, out var baseIsExact))
            {
                hits.Add(Wrap(type.EcmaId, new ApiReferenceHit(
                    type.FullName, ApiReferenceKind.Base, null, type.BaseType.TypeExpression, null,
                    baseIsExact, null, _provenance)));
            }
            foreach (var interfaceUse in type.Interfaces.OrderBy(
                         item => item.TypeExpression, StringComparer.Ordinal))
            {
                if (ReferencesTypeUse(interfaceUse, symbol, out var interfaceIsExact))
                {
                    hits.Add(Wrap(type.EcmaId, new ApiReferenceHit(
                        type.FullName, ApiReferenceKind.Interface, null, interfaceUse.TypeExpression, null,
                        interfaceIsExact, null, _provenance)));
                }
            }
            AddTypeUseHits(
                hits, type.Constraints, symbol, type.EcmaId, type.FullName, ApiReferenceKind.Constraint, null);
            AddAttributeHits(
                hits, type.Attributes, symbol, siblingType, type.EcmaId, type.FullName, null,
                ref siblingApplications);

            foreach (var member in type.Members
                         .OrderBy(item => item.Name, StringComparer.Ordinal)
                         .ThenBy(item => item.Signature, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memberSymbol = $"{type.FullName}.{member.Name}";
                AddTypeUseHits(
                    hits, member.Constraints, symbol, member.EcmaId, memberSymbol,
                    ApiReferenceKind.Constraint, member.Signature);
                AddAttributeHits(
                    hits, member.Attributes, symbol, siblingType, member.EcmaId, memberSymbol, member.Signature,
                    ref siblingApplications);
                if (member.ReturnType is not null
                    && ReferencesTypeUse(member.ReturnType, symbol, out var returnIsExact))
                {
                    hits.Add(Wrap(member.EcmaId, new ApiReferenceHit(
                        memberSymbol, ApiReferenceKind.Return, null,
                        member.ReturnType.TypeExpression, null, returnIsExact,
                        member.Signature, _provenance)));
                }
                foreach (var parameter in member.Parameters)
                {
                    if (ReferencesTypeUse(parameter, symbol, out var parameterIsExact))
                    {
                        hits.Add(Wrap(member.EcmaId, new ApiReferenceHit(
                            memberSymbol, ApiReferenceKind.Parameter, parameter.Name,
                            parameter.TypeExpression, null, parameterIsExact,
                            member.Signature, _provenance)));
                    }
                }
            }
        }
        return new ApiReferenceRead(hits, siblingType, siblingApplications, _coverage);
    }

    private ApiCorpusType[] FindTypes(string symbol)
    {
        var qualified = symbol.Contains('.', StringComparison.Ordinal);
        return _corpus.Types.Where(type => qualified
                ? TypeSelectorMatches(type.FullName, type.EcmaId, symbol)
                : TypeSelectorMatches(type.Name, SimpleEcmaName(type.EcmaId), symbol))
            .ToArray();
    }

    private ApiLookupTypeRead ReadType(ApiCorpusType type, string? memberName)
    {
        var members = type.Members
            .Where(member => memberName is null || MemberNameMatches(member.Name, memberName))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Signature, StringComparer.Ordinal)
            .Select(member => new ApiLookupMemberRead(member.EcmaId, ReadMember(member)))
            .ToArray();
        var documentation = new ApiTypeDocumentation(
            type.FullName,
            members.Select(member => member.Documentation).ToArray(),
            _provenance,
            memberName is null ? ApiLookupDetail.Signatures : ApiLookupDetail.Full);
        return new ApiLookupTypeRead(type.EcmaId, documentation, members);
    }

    private ApiMemberDocumentation ReadMember(ApiCorpusMember member)
    {
        var descriptions = member.Documentation.Parameters.ToDictionary(
            item => item.Name, item => item.Text, StringComparer.Ordinal);
        return new ApiMemberDocumentation(
            member.Name,
            member.Signature,
            member.Documentation.Summary,
            member.Parameters.Select(parameter => new ApiParameterDocumentation(
                parameter.Name ?? string.Empty,
                parameter.Name is not null && descriptions.TryGetValue(parameter.Name, out var description)
                    ? description
                    : null)).ToArray(),
            member.Documentation.Returns,
            member.Documentation.Remarks,
            _provenance);
    }

    private void AddDocumentationHits(
        ICollection<ApiTextHitRead> hits,
        string declarationId,
        string symbol,
        ApiDocumentation docs,
        string query)
    {
        AddTextHit(hits, declarationId, symbol, "summary", docs.Summary, query);
        foreach (var parameter in docs.Parameters)
            AddTextHit(hits, declarationId, symbol, $"param:{parameter.Name}", parameter.Text, query);
        foreach (var parameter in docs.TypeParameters)
            AddTextHit(hits, declarationId, symbol, $"typeparam:{parameter.Name}", parameter.Text, query);
        AddTextHit(hits, declarationId, symbol, "returns", docs.Returns, query);
        AddTextHit(hits, declarationId, symbol, "value", docs.Value, query);
        AddTextHit(hits, declarationId, symbol, "remarks", docs.Remarks, query);
        foreach (var exception in docs.Exceptions)
            AddTextHit(hits, declarationId, symbol, "exception", exception.Text, query);
    }

    private void AddTextHit(
        ICollection<ApiTextHitRead> hits,
        string declarationId,
        string symbol,
        string element,
        string? value,
        string query)
    {
        if (value is null || !value.Contains(query, StringComparison.OrdinalIgnoreCase))
            return;
        var (text, isTruncated) = DocumentationText.Budget(value, MatchTextBudget);
        hits.Add(new ApiTextHitRead(
            declarationId,
            new ApiTextHit(symbol, element, text, isTruncated, _provenance)));
    }

    private void AddTypeUseHits(
        List<ApiReferenceHitRead> hits,
        IEnumerable<ApiTypeUse> uses,
        string symbol,
        string declarationId,
        string owningSymbol,
        string kind,
        string? signature)
    {
        foreach (var use in uses.OrderBy(item => item.TypeExpression, StringComparer.Ordinal))
        {
            if (ReferencesTypeUse(use, symbol, out var isExact))
            {
                hits.Add(Wrap(declarationId, new ApiReferenceHit(
                    owningSymbol, kind, use.Name, use.TypeExpression, null,
                    isExact, signature, _provenance)));
            }
        }
    }

    private void AddAttributeHits(
        List<ApiReferenceHitRead> hits,
        IEnumerable<ApiAttributeUse> attributes,
        string symbol,
        string? siblingType,
        string declarationId,
        string owningSymbol,
        string? signature,
        ref int siblingApplications)
    {
        foreach (var attribute in attributes
                     .OrderBy(item => item.Application, StringComparer.Ordinal)
                     .ThenBy(item => item.AttributeType, StringComparer.Ordinal)
                     .DistinctBy(
                         item => item.Application + "\0" + item.AttributeType + "\0"
                             + string.Join("\0", item.ArgumentTypeNames),
                         StringComparer.Ordinal))
        {
            var namesTheAttribute = string.Equals(attribute.AttributeType, symbol, StringComparison.Ordinal);
            var appliedName = AttributeTypeName(attribute.Application);
            if (!namesTheAttribute
                && siblingType is not null
                && string.Equals(attribute.AttributeType, siblingType, StringComparison.Ordinal)
                && string.Equals(appliedName, symbol, StringComparison.Ordinal))
            {
                siblingApplications++;
                continue;
            }

            if (!namesTheAttribute
                && !attribute.ArgumentTypeNames.Contains(symbol, StringComparer.Ordinal))
            {
                continue;
            }
            hits.Add(Wrap(declarationId, new ApiReferenceHit(
                owningSymbol, ApiReferenceKind.Attribute, null, attribute.Application,
                attribute.AttributeType, namesTheAttribute, signature, _provenance)));
        }
    }

    private bool TypeExists(string typeName) =>
        _corpus.Types.Any(type => string.Equals(type.FullName, typeName, StringComparison.Ordinal));

    private static ApiReferenceHitRead Wrap(string declarationId, ApiReferenceHit hit) =>
        new(declarationId, hit);

    private static bool ReferencesTypeUse(ApiTypeUse use, string symbol, out bool isExact)
    {
        if (!use.TypeNames.Contains(symbol, StringComparer.Ordinal))
        {
            isExact = false;
            return false;
        }
        isExact = IsExactExpression(use.TypeExpression, symbol);
        return true;
    }

    private static bool IsExactExpression(string expression, string symbol) =>
        string.Equals(expression, symbol, StringComparison.Ordinal)
        || CSharpAliases.TryGetValue(symbol, out var alias)
            && string.Equals(expression, alias, StringComparison.Ordinal);

    private static bool TypeSelectorMatches(string displayName, string ecmaName, string symbol) =>
        string.Equals(displayName, symbol, StringComparison.Ordinal)
        || string.Equals(StripGenericArguments(displayName), symbol, StringComparison.Ordinal)
        || string.Equals(ecmaName, symbol, StringComparison.Ordinal)
        || string.Equals(StripGenericArity(ecmaName), symbol, StringComparison.Ordinal);

    private static string SimpleEcmaName(string ecmaId)
    {
        var canonical = CanonicalEcmaName(ecmaId);
        var separator = canonical.LastIndexOf('.');
        return separator < 0 ? canonical : canonical[(separator + 1)..];
    }

    private static string CanonicalEcmaName(string ecmaId) =>
        ecmaId.StartsWith("T:", StringComparison.Ordinal) ? ecmaId[2..] : ecmaId;

    private static string StripGenericArity(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        for (var index = 0; index < name.Length; index++)
        {
            if (name[index] != '`')
            {
                builder.Append(name[index]);
                continue;
            }
            while (index + 1 < name.Length && char.IsDigit(name[index + 1]))
                index++;
        }
        return builder.ToString();
    }

    private static string StripGenericArguments(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        var depth = 0;
        foreach (var character in name)
        {
            if (character == '<')
            {
                depth++;
                continue;
            }
            if (character == '>')
            {
                depth--;
                continue;
            }
            if (depth == 0)
                builder.Append(character);
        }
        return builder.ToString();
    }

    private static bool MemberNameMatches(string name, string requested)
    {
        if (string.Equals(name, requested, StringComparison.Ordinal))
            return true;
        var typeParameters = name.IndexOf('<', StringComparison.Ordinal);
        return typeParameters > 0
            && string.Equals(name[..typeParameters], requested, StringComparison.Ordinal);
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
        if (run.Length == 0 || run.Length > segments.Length || run.Any(segment => segment.Length == 0))
            return -1;
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

    private IEnumerable<ApiCorpusType> OrderedTypes() => _corpus.Types
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ThenBy(type => type.EcmaId, StringComparer.Ordinal);

    private static string NamespaceName(string fullName)
    {
        var separator = fullName.LastIndexOf('.');
        return separator < 0 ? string.Empty : fullName[..separator];
    }

    private string FindNamespace(ApiCorpusType type)
    {
        var canonicalName = CanonicalEcmaName(type.EcmaId);
        var containingType = _corpus.Types
            .Where(candidate => !ReferenceEquals(candidate, type))
            .Where(candidate => canonicalName.StartsWith(
                CanonicalEcmaName(candidate.EcmaId) + ".",
                StringComparison.Ordinal))
            .Where(candidate => type.FullName.StartsWith(candidate.FullName + ".", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.FullName.Length)
            .FirstOrDefault();
        if (containingType is not null)
            return FindNamespace(containingType);

        if (string.Equals(type.FullName, type.Name, StringComparison.Ordinal))
            return string.Empty;
        var suffix = "." + type.Name;
        return type.FullName.EndsWith(suffix, StringComparison.Ordinal)
            ? type.FullName[..^suffix.Length]
            : NamespaceName(canonicalName);
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

    private static ApiPackageDefinition JoinDefinition(SourceSnapshot snapshot, string packageId)
    {
        var matches = (snapshot.Definition.ApiPackages ?? [])
            .Where(item => item is not null
                && string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"The synchronized snapshot must configure exactly one API package named '{packageId}'.");
    }

    private static ApiPackageSyncState JoinState(SourceSnapshot snapshot, string packageId)
    {
        var matches = (snapshot.State.ApiPackages ?? [])
            .Where(item => item is not null
                && string.Equals(item.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"The synchronized snapshot must contain exactly one API package state named '{packageId}'.");
    }

    private static void ValidateSnapshotIdentity(SourceSnapshot snapshot)
    {
        if (!SourceCache.IsComplete(snapshot.State)
            || !string.Equals(snapshot.Definition.Repository, snapshot.State.Repository, StringComparison.Ordinal)
            || !string.Equals(snapshot.Definition.Url, snapshot.State.Url, StringComparison.Ordinal)
            || !snapshot.State.SparsePaths.SequenceEqual(snapshot.Definition.Sparse, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The synchronized source state does not match its catalog definition.");
        }
        if (string.Equals(snapshot.State.Ref, "pinned", StringComparison.Ordinal))
        {
            if (!string.Equals(snapshot.State.Commit, snapshot.Definition.Pin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The pinned synchronized source commit does not match its catalog pin.");
            return;
        }
        if (!string.Equals(
                snapshot.State.Ref,
                $"head:{snapshot.Definition.Head}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported or stale synchronized source ref '{snapshot.State.Ref}'.");
        }
    }

    private static void ValidatePackageIdentity(
        SourceSnapshot snapshot,
        ApiPackageDefinition definition,
        ApiPackageSyncState state)
    {
        if (!IsValidDefinition(definition)
            || !string.Equals(definition.PackageId, state.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(definition.AssemblyName, state.AssemblyName, StringComparison.Ordinal)
            || !string.Equals(definition.Feed, state.Feed, StringComparison.Ordinal)
            || !string.Equals(definition.DefaultFramework, state.DefaultFramework, StringComparison.Ordinal)
            || state.FetchedAt == default
            || !NuGetPackageIdentity.IsValidVersion(state.Version)
            || !HasValidSha512(state.Sha512)
            || string.IsNullOrWhiteSpace(state.CorpusDirectory)
            || state.AvailableFrameworks is null
            || state.AvailableFrameworks.Count == 0
            || state.AvailableFrameworks.Any(string.IsNullOrWhiteSpace)
            || state.AvailableFrameworks.Any(framework => !PortableFrameworkName.IsSafe(framework))
            || state.AvailableFrameworks.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != state.AvailableFrameworks.Count
            || !state.AvailableFrameworks.SequenceEqual(
                state.AvailableFrameworks.OrderBy(item => item, StringComparer.Ordinal),
                StringComparer.Ordinal)
            || !state.AvailableFrameworks.Contains(state.DefaultFramework, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The synchronized package state does not match its catalog definition.");
        }
        if (string.Equals(snapshot.State.Ref, "pinned", StringComparison.Ordinal)
            && (!string.Equals(definition.Version, state.Version, StringComparison.Ordinal)
                || !string.Equals(definition.Sha512, state.Sha512, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The pinned synchronized package identity does not match its catalog definition.");
        }
    }

    private static bool IsValidDefinition(ApiPackageDefinition definition) =>
        NuGetPackageIdentity.IsValidPackageId(definition.PackageId)
        && !string.IsNullOrWhiteSpace(definition.AssemblyName)
        && definition.AssemblyName.IndexOfAny(['/', '\\', ':']) < 0
        && Uri.TryCreate(definition.Feed, UriKind.Absolute, out var feed)
        && string.Equals(feed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(definition.Version)
        && definition.Version.All(character => char.IsAsciiDigit(character) || character == '.')
        && Version.TryParse(definition.Version, out _)
        && HasValidSha512(definition.Sha512)
        && !string.IsNullOrWhiteSpace(definition.DefaultFramework);

    private static bool HasValidSha512(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ResolveContainedDirectory(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("The package corpus directory must be relative.");
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var relative = Path.GetRelativePath(fullRoot, candidate);
        if (relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                "The package corpus directory resolves outside the synchronized supplement root.");
        }
        return candidate;
    }

    private static string ResolveCorpusPath(string corpusDirectory, string framework)
    {
        if (!PortableFrameworkName.IsSafe(framework))
        {
            throw new InvalidDataException("The package framework is not a valid corpus file name.");
        }
        var path = Path.GetFullPath(Path.Combine(corpusDirectory, framework + ".json"));
        var prefix = Path.TrimEndingDirectorySeparator(corpusDirectory) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
            throw new InvalidDataException("The package framework corpus resolves outside its directory.");
        return path;
    }
}
