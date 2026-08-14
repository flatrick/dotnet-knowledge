using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

internal sealed record PackageApiCorpusLimits(
    long MaxFileBytes,
    int MaxTypes,
    int MaxMembersPerType,
    int MaxTotalMembers,
    int MaxCollectionItems,
    int MaxStringChars,
    int MaxStringBytes,
    int MaxJsonDepth)
{
    // The configured Roslyn package currently produces a roughly 50 KB / 6-type / 48-member
    // corpus per framework. A 16 MiB file permits more than 300 times that measured output while
    // keeping the one pre-deserialization input allocation bounded. The structural limits retain
    // several orders of magnitude of headroom over the measured 6 types, 48 total members, and 28
    // members on one type.
    internal static PackageApiCorpusLimits Default { get; } = new(
        MaxFileBytes: 16L * 1024 * 1024,
        MaxTypes: 10_000,
        MaxMembersPerType: 10_000,
        MaxTotalMembers: 100_000,
        MaxCollectionItems: 10_000,
        MaxStringChars: 1_048_576,
        MaxStringBytes: 1_048_576,
        MaxJsonDepth: 32);
}

public static class PackageApiCorpusStore
{
    private const int SchemaVersion = 3;
    private const int MaximumConfiguredJsonDepth = 64;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<CacheKey, CacheEntry> Cache = [];

    public static ApiCorpus Read(string path, ApiPackageDefinition definition, string framework)
        => Read(path, definition, framework, PackageApiCorpusLimits.Default);

    internal static ApiCorpus Read(
        string path,
        ApiPackageDefinition definition,
        string framework,
        PackageApiCorpusLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits);
        var key = new CacheKey(definition.PackageId, definition.Version, definition.Sha512, framework);
        var bytes = ReadBounded(path, limits.MaxFileBytes);
        PreflightJson(bytes, limits);
        StoredCorpus stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredCorpus>(bytes)
                ?? throw new InvalidDataException("The package corpus is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The package corpus JSON is malformed.", exception);
        }
        if (stored.SchemaVersion != SchemaVersion || stored.Corpus?.SchemaVersion != SchemaVersion)
            throw new InvalidDataException(
                "The package corpus has an unsupported schema version; resynchronize the source.");
        if (!string.Equals(stored.PackageId, definition.PackageId, StringComparison.Ordinal)
            || !string.Equals(stored.Version, definition.Version, StringComparison.Ordinal)
            || !string.Equals(stored.Sha512, definition.Sha512, StringComparison.Ordinal)
            || !string.Equals(stored.Framework, framework, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The package corpus identity does not match the requested package framework.");
        }
        ValidateCorpus(stored.Corpus, limits);

        var contentHash = Convert.ToHexString(SHA256.HashData(bytes));
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached) && cached.ContentHash == contentHash)
                return cached.Corpus;

            Cache[key] = new CacheEntry(contentHash, stored.Corpus);
            return stored.Corpus;
        }
    }

    private static void ValidateLimits(PackageApiCorpusLimits limits)
    {
        if (limits.MaxFileBytes < 0 || limits.MaxFileBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(limits), "The maximum file size is outside the supported range.");
        if (limits.MaxTypes < 0
            || limits.MaxMembersPerType < 0
            || limits.MaxTotalMembers < 0
            || limits.MaxCollectionItems < 0
            || limits.MaxStringChars < 0
            || limits.MaxStringBytes < 0
            || limits.MaxJsonDepth is < 1 or > MaximumConfiguredJsonDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "One or more package corpus limits are outside the supported range.");
        }
    }

    private static void PreflightJson(ReadOnlySpan<byte> bytes, PackageApiCorpusLimits limits)
    {
        var options = new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = limits.MaxJsonDepth,
        };
        var reader = new Utf8JsonReader(bytes, options);
        var containers = new JsonContainerFrame[limits.MaxJsonDepth];
        var containerCount = 0;
        var pendingProperty = JsonPropertyKind.Other;
        long totalTypes = 0;
        long totalMembers = 0;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.String)
                    ValidateJsonString(reader.ValueSpan, limits);
                else if (reader.TokenType == JsonTokenType.Number
                         && reader.ValueSpan.Length > limits.MaxStringBytes)
                    Invalid($"JSON preflight token exceeds the {limits.MaxStringBytes}-byte limit");

                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pendingProperty = reader.ValueTextEquals("Types"u8)
                            ? JsonPropertyKind.Types
                            : reader.ValueTextEquals("Members"u8)
                                ? JsonPropertyKind.Members
                                : JsonPropertyKind.Other;
                        break;
                    case JsonTokenType.StartObject:
                        CountArrayItem(containers, containerCount, limits, ref totalTypes, ref totalMembers);
                        PushContainer(
                            containers,
                            ref containerCount,
                            new JsonContainerFrame(isArray: false, JsonPropertyKind.Other));
                        pendingProperty = JsonPropertyKind.Other;
                        break;
                    case JsonTokenType.StartArray:
                        CountArrayItem(containers, containerCount, limits, ref totalTypes, ref totalMembers);
                        PushContainer(
                            containers,
                            ref containerCount,
                            new JsonContainerFrame(isArray: true, pendingProperty));
                        pendingProperty = JsonPropertyKind.Other;
                        break;
                    case JsonTokenType.EndObject:
                        PopContainer(containers, ref containerCount, isArray: false);
                        pendingProperty = JsonPropertyKind.Other;
                        break;
                    case JsonTokenType.EndArray:
                        PopContainer(containers, ref containerCount, isArray: true);
                        pendingProperty = JsonPropertyKind.Other;
                        break;
                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        CountArrayItem(containers, containerCount, limits, ref totalTypes, ref totalMembers);
                        pendingProperty = JsonPropertyKind.Other;
                        break;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The package corpus JSON failed bounded preflight.", exception);
        }
        if (containerCount != 0)
            Invalid("JSON preflight ended with an incomplete container");
    }

    private static void ValidateJsonString(
        ReadOnlySpan<byte> encoded,
        PackageApiCorpusLimits limits)
    {
        if (encoded.Length > limits.MaxStringBytes)
            Invalid($"JSON preflight string exceeds the {limits.MaxStringBytes}-byte limit");

        var characters = 0;
        for (var index = 0; index < encoded.Length;)
        {
            int increment;
            if (encoded[index] != (byte)'\\')
            {
                var status = Rune.DecodeFromUtf8(encoded[index..], out var rune, out var consumed);
                if (status != OperationStatus.Done)
                    Invalid("JSON preflight found invalid UTF-8");
                increment = rune.Utf16SequenceLength;
                index += consumed;
            }
            else
            {
                index++;
                if (index >= encoded.Length)
                    Invalid("JSON preflight found an incomplete escape");
                var escape = encoded[index++];
                if (escape != (byte)'u')
                {
                    increment = 1;
                }
                else
                {
                    if (!TryReadEscapedCodeUnit(encoded, ref index, out var codeUnit))
                        Invalid("JSON preflight found an invalid Unicode escape");
                    if (char.IsHighSurrogate((char)codeUnit))
                    {
                        if (index + 2 > encoded.Length
                            || encoded[index] != (byte)'\\'
                            || encoded[index + 1] != (byte)'u')
                        {
                            Invalid("JSON preflight found an unpaired high surrogate");
                        }
                        index += 2;
                        if (!TryReadEscapedCodeUnit(encoded, ref index, out var low)
                            || !char.IsLowSurrogate((char)low))
                        {
                            Invalid("JSON preflight found an unpaired high surrogate");
                        }
                        increment = 2;
                    }
                    else
                    {
                        if (char.IsLowSurrogate((char)codeUnit))
                            Invalid("JSON preflight found an unpaired low surrogate");
                        increment = 1;
                    }
                }
            }

            if (characters > limits.MaxStringChars - increment)
                Invalid($"JSON preflight string exceeds the {limits.MaxStringChars}-character limit");
            characters += increment;
        }
    }

    private static bool TryReadEscapedCodeUnit(
        ReadOnlySpan<byte> encoded,
        ref int index,
        out int value)
    {
        value = 0;
        if (index > encoded.Length - 4)
            return false;
        for (var offset = 0; offset < 4; offset++)
        {
            var digit = encoded[index++];
            var nibble = digit switch
            {
                >= (byte)'0' and <= (byte)'9' => digit - (byte)'0',
                >= (byte)'A' and <= (byte)'F' => digit - (byte)'A' + 10,
                >= (byte)'a' and <= (byte)'f' => digit - (byte)'a' + 10,
                _ => -1,
            };
            if (nibble < 0)
                return false;
            value = (value << 4) | nibble;
        }
        return true;
    }

    private static void CountArrayItem(
        JsonContainerFrame[] containers,
        int containerCount,
        PackageApiCorpusLimits limits,
        ref long totalTypes,
        ref long totalMembers)
    {
        if (containerCount == 0 || !containers[containerCount - 1].IsArray)
            return;
        ref var frame = ref containers[containerCount - 1];
        var maximum = frame.PropertyKind switch
        {
            JsonPropertyKind.Types => limits.MaxTypes,
            JsonPropertyKind.Members => limits.MaxMembersPerType,
            _ => limits.MaxCollectionItems,
        };
        if (frame.ItemCount >= maximum)
            Invalid($"JSON preflight array exceeds the {maximum}-item limit");
        frame.ItemCount++;
        if (frame.PropertyKind == JsonPropertyKind.Types)
        {
            if (totalTypes >= limits.MaxTypes)
                Invalid($"JSON preflight types exceed the {limits.MaxTypes}-item total limit");
            totalTypes++;
        }
        else if (frame.PropertyKind == JsonPropertyKind.Members)
        {
            if (totalMembers >= limits.MaxTotalMembers)
                Invalid($"JSON preflight members exceed the {limits.MaxTotalMembers}-item total limit");
            totalMembers++;
        }
    }

    private static void PushContainer(
        JsonContainerFrame[] containers,
        ref int count,
        JsonContainerFrame frame)
    {
        if (count >= containers.Length)
            Invalid("JSON preflight exceeds the configured depth limit");
        containers[count++] = frame;
    }

    private static void PopContainer(
        JsonContainerFrame[] containers,
        ref int count,
        bool isArray)
    {
        if (count == 0 || containers[count - 1].IsArray != isArray)
            Invalid("JSON preflight found mismatched containers");
        count--;
    }

    private static byte[] ReadBounded(string path, long maxFileBytes)
    {
        if (maxFileBytes < 0 || maxFileBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxFileBytes)
            Invalid($"file exceeds the {maxFileBytes}-byte limit");
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            Invalid($"file exceeds the {maxFileBytes}-byte limit");
        return bytes;
    }

    internal static async Task WriteAsync(
        string path,
        ApiPackageDefinition definition,
        string framework,
        ApiCorpus corpus,
        CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new StoredCorpus(SchemaVersion, definition.PackageId, definition.Version, definition.Sha512,
                        framework, corpus),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static int GetCachedVariantCount(ApiPackageDefinition definition, string framework)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        var key = new CacheKey(definition.PackageId, definition.Version, definition.Sha512, framework);
        lock (CacheLock)
            return Cache.ContainsKey(key) ? 1 : 0;
    }

    private sealed record CacheKey(string PackageId, string Version, string Sha512, string Framework);

    private sealed record CacheEntry(string ContentHash, ApiCorpus Corpus);

    private sealed record StoredCorpus(
        int SchemaVersion, string PackageId, string Version, string Sha512, string Framework, ApiCorpus? Corpus);

    private enum JsonPropertyKind
    {
        Other,
        Types,
        Members,
    }

    private struct JsonContainerFrame
    {
        internal JsonContainerFrame(bool isArray, JsonPropertyKind propertyKind)
        {
            IsArray = isArray;
            PropertyKind = propertyKind;
        }

        internal bool IsArray { get; }
        internal JsonPropertyKind PropertyKind { get; }
        internal int ItemCount { get; set; }
    }

    private static void ValidateCorpus(ApiCorpus corpus, PackageApiCorpusLimits limits)
    {
        if (corpus.Types is null)
            Invalid("types must be present");
        ValidateSkipped(corpus.Skipped, limits);
        RequireCount(corpus.Types, limits.MaxTypes, "types");
        RequireOrdered(corpus.Types, type => type.FullName, "types");
        var typeIds = new HashSet<string>(StringComparer.Ordinal);
        var memberIds = new HashSet<string>(StringComparer.Ordinal);
        long totalMembers = 0;
        foreach (var type in corpus.Types)
        {
            if (type is null)
                Invalid("types cannot contain null values");
            RequireBoundedString(type.EcmaId, "type ID", limits);
            if (!ApiDeclarationId.IsCanonicalTypeId(type.EcmaId))
                Invalid($"type ID '{type.EcmaId}' is not canonical");
            if (!typeIds.Add(type.EcmaId))
                Invalid($"type ID '{type.EcmaId}' is duplicated");
            RequireText(type.Name, "type name", limits);
            RequireText(type.FullName, "type full name", limits);
            RequireLists(type.Interfaces, type.Constraints, type.Attributes, type.Members);
            RequireCount(type.Interfaces, limits.MaxCollectionItems, "interfaces");
            RequireCount(type.Constraints, limits.MaxCollectionItems, "type constraints");
            RequireCount(type.Attributes, limits.MaxCollectionItems, "type attributes");
            RequireCount(type.Members, limits.MaxMembersPerType, "members");
            totalMembers += type.Members.Count;
            if (totalMembers > limits.MaxTotalMembers)
                Invalid($"members exceed the {limits.MaxTotalMembers}-item total limit");
            if (type.BaseType is not null)
                ValidateTypeUse(type.BaseType, "base type", limits, nameRequired: false);
            ValidateTypeUses(type.Interfaces, "interfaces", ordered: true, limits, namesRequired: false);
            ValidateTypeUses(type.Constraints, "type constraints", ordered: true, limits, namesRequired: true);
            ValidateAttributes(type.Attributes, "type attributes", limits);
            ValidateDocumentation(type.Documentation, "type documentation", limits);
            RequireOrdered(type.Members, member => member.EcmaId, "members");
            foreach (var member in type.Members)
            {
                if (member is null)
                    Invalid("members cannot contain null values");
                RequireBoundedString(member.EcmaId, "member ID", limits);
                if (!ApiDeclarationId.IsCanonicalMemberId(member.EcmaId, type.EcmaId))
                    Invalid($"member ID '{member.EcmaId}' is not canonical for '{type.EcmaId}'");
                if (!memberIds.Add(member.EcmaId))
                    Invalid($"member ID '{member.EcmaId}' is duplicated");
                RequireText(member.Name, "member name", limits);
                RequireText(member.Kind, "member kind", limits);
                RequireText(member.Signature, "member signature", limits);
                RequireLists(member.Parameters, member.Constraints, member.Attributes);
                RequireCount(member.Parameters, limits.MaxCollectionItems, "member parameters");
                RequireCount(member.Constraints, limits.MaxCollectionItems, "member constraints");
                RequireCount(member.Attributes, limits.MaxCollectionItems, "member attributes");
                ValidateTypeUses(member.Parameters, "member parameters", ordered: false, limits, namesRequired: true);
                if (member.ReturnType is not null)
                    ValidateTypeUse(member.ReturnType, "member return type", limits, nameRequired: false);
                ValidateTypeUses(member.Constraints, "member constraints", ordered: true, limits, namesRequired: true);
                ValidateAttributes(member.Attributes, "member attributes", limits);
                ValidateDocumentation(member.Documentation, "member documentation", limits);
            }
        }
    }

    /// <summary>
    /// Skipped declarations are validated with the same rigor as the corpus proper. They are the
    /// record of what a query does not cover, so a malformed one understates a coverage gap.
    /// </summary>
    private static void ValidateSkipped(
        IReadOnlyList<ApiSkippedDeclaration> skipped,
        PackageApiCorpusLimits limits)
    {
        if (skipped is null)
            Invalid("skipped declarations must be present");
        RequireCount(skipped, limits.MaxTotalMembers, "skipped declarations");
        foreach (var declaration in skipped)
        {
            if (declaration is null)
                Invalid("skipped declarations cannot contain null values");
            RequireText(declaration.Kind, "skipped declaration kind", limits);
            RequireText(declaration.DeclaringType, "skipped declaration declaring type", limits);
            if (declaration.Name is not null)
                RequireText(declaration.Name, "skipped declaration name", limits);
            RequireText(declaration.Reason, "skipped declaration reason", limits);
        }
    }

    private static void ValidateTypeUses(
        IReadOnlyList<ApiTypeUse> uses,
        string description,
        bool ordered,
        PackageApiCorpusLimits limits,
        bool namesRequired)
    {
        if (ordered)
            RequireOrdered(uses, use => use.TypeExpression, description);
        foreach (var use in uses)
        {
            if (use is null)
                Invalid($"{description} cannot contain null values");
            ValidateTypeUse(use, description, limits, namesRequired);
        }
    }

    private static void ValidateTypeUse(
        ApiTypeUse use,
        string description,
        PackageApiCorpusLimits limits,
        bool nameRequired)
    {
        if (nameRequired)
        {
            if (!ApiDeclarationId.IsIdentifier(use.Name ?? string.Empty))
                Invalid($"{description} name must be a canonical identifier");
            RequireText(use.Name, $"{description} name", limits);
        }
        else if (use.Name is not null)
        {
            if (!ApiDeclarationId.IsIdentifier(use.Name))
                Invalid($"{description} name must be a canonical identifier");
            RequireText(use.Name, $"{description} name", limits);
        }
        RequireText(use.TypeExpression, $"{description} expression", limits);
        if (use.TypeNames is null)
            Invalid($"{description} canonical type names must be present");
        RequireCount(use.TypeNames, limits.MaxCollectionItems, $"{description} canonical type names");
        RequireOrdered(use.TypeNames, name => name, $"{description} canonical type names");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in use.TypeNames)
        {
            RequireBoundedString(name, $"{description} canonical type name", limits);
            if (!ApiDeclarationId.IsCanonicalTypeName(name))
                Invalid($"{description} canonical type name '{name}' is invalid");
            if (!names.Add(name))
                Invalid($"{description} canonical type name '{name}' is duplicated");
        }
    }

    private static void ValidateAttributes(
        IReadOnlyList<ApiAttributeUse> attributes,
        string description,
        PackageApiCorpusLimits limits)
    {
        RequireOrdered(attributes, AttributeKey, description);
        foreach (var attribute in attributes)
        {
            if (attribute is null)
                Invalid($"{description} cannot contain null values");
            RequireText(attribute.Application, $"{description} application", limits);
            RequireBoundedString(attribute.AttributeType, $"{description} canonical attribute type", limits);
            if (!ApiDeclarationId.IsCanonicalTypeName(attribute.AttributeType))
                Invalid($"{description} canonical attribute type is invalid");
            if (attribute.ArgumentTypeNames is null)
                Invalid($"{description} argument type names must be present");
            RequireCount(attribute.ArgumentTypeNames, limits.MaxCollectionItems, $"{description} argument type names");
            RequireOrdered(attribute.ArgumentTypeNames, name => name, $"{description} argument type names");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in attribute.ArgumentTypeNames)
            {
                RequireBoundedString(name, $"{description} argument type name", limits);
                if (!ApiDeclarationId.IsCanonicalTypeName(name) || !names.Add(name))
                    Invalid($"{description} argument type name '{name}' is invalid or duplicated");
            }
        }
    }

    private static string AttributeKey(ApiAttributeUse attribute) =>
        attribute.Application + "\0" + attribute.AttributeType;

    private static void ValidateDocumentation(
        ApiDocumentation? documentation,
        string description,
        PackageApiCorpusLimits limits)
    {
        if (documentation is null)
            Invalid($"{description} must be present");
        RequireLists(
            documentation.Parameters,
            documentation.TypeParameters,
            documentation.Exceptions);
        RequireOptionalString(documentation.Summary, $"{description} summary", limits);
        RequireOptionalString(documentation.Returns, $"{description} returns", limits);
        RequireOptionalString(documentation.Value, $"{description} value", limits);
        RequireOptionalString(documentation.Remarks, $"{description} remarks", limits);
        ValidateNamedDocumentation(
            documentation.Parameters, $"{description} parameters", limits, NamedDocumentationKind.Identifier);
        ValidateNamedDocumentation(
            documentation.TypeParameters, $"{description} type parameters", limits, NamedDocumentationKind.Identifier);
        ValidateNamedDocumentation(
            documentation.Exceptions, $"{description} exceptions", limits, NamedDocumentationKind.TypeId);
    }

    private static void ValidateNamedDocumentation(
        IReadOnlyList<ApiNamedDocumentation> items,
        string description,
        PackageApiCorpusLimits limits,
        NamedDocumentationKind kind)
    {
        RequireCount(items, limits.MaxCollectionItems, description);
        RequireOrdered(items, item => item.Name + "\0" + item.Text, description);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
                Invalid($"{description} cannot contain null values");
            RequireText(item.Name, $"{description} name", limits);
            if (kind == NamedDocumentationKind.Identifier
                ? !ApiDeclarationId.IsIdentifier(item.Name)
                : !ApiDeclarationId.IsCanonicalTypeId(item.Name))
            {
                Invalid($"{description} name '{item.Name}' is not canonical");
            }
            if (!names.Add(item.Name))
                Invalid($"{description} name '{item.Name}' is duplicated");
            RequireBoundedString(item.Text, $"{description} text", limits);
        }
    }

    private static void RequireText(
        string? value,
        string description,
        PackageApiCorpusLimits limits)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            Invalid($"{description} must be nonblank and trimmed");
        }
        RequireBoundedString(value, description, limits);
    }

    private static void RequireOptionalString(
        string? value,
        string description,
        PackageApiCorpusLimits limits)
    {
        if (value is not null)
            RequireBoundedString(value, description, limits);
    }

    private static void RequireBoundedString(
        string? value,
        string description,
        PackageApiCorpusLimits limits)
    {
        if (value is null)
            Invalid($"{description} must be present");
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxStringChars);
        if (value.Length > limits.MaxStringChars)
            Invalid($"{description} exceeds the {limits.MaxStringChars}-character limit");
    }

    private static void RequireCount<T>(
        IReadOnlyList<T> items,
        int maximum,
        string description)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        if (items.Count > maximum)
            Invalid($"{description} exceeds the {maximum}-item limit");
    }

    private static void RequireLists(params object?[] lists)
    {
        if (lists.Any(list => list is null))
            Invalid("required corpus collections must be present");
    }

    private static void RequireOrdered<T>(
        IReadOnlyList<T> items,
        Func<T, string> key,
        string description)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is null)
                Invalid($"{description} cannot contain null values");
        }
        for (var index = 1; index < items.Count; index++)
        {
            if (StringComparer.Ordinal.Compare(key(items[index - 1]), key(items[index])) > 0)
                Invalid($"{description} must be ordinally ordered");
        }
    }

    [DoesNotReturn]
    private static void Invalid(string message) =>
        throw new InvalidDataException("The package corpus is invalid: " + message + ".");

    private enum NamedDocumentationKind
    {
        Identifier,
        TypeId,
    }
}
