using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

[McpServerToolType]
public sealed class ApiDocsTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        // Indentation is roughly a fifth of every response's bytes and buys an agent nothing.
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string FrameworkDescription =
        "Part of the Roslyn coverage comes from a pinned NuGet package, which declares one API " +
        "surface per target framework; pass framework to choose which - net472, net8.0, net9.0 or " +
        "net10.0 - and omit it for the cataloged default. Every response reports the " +
        "effectiveFramework it queried beside defaultFramework and availableFrameworks, and an " +
        "unavailable value returns framework_not_available listing them rather than an empty " +
        "result. The repository-sourced documentation is framework-neutral, so framework applies " +
        "to the package half of the coverage only and is rejected with source: \"dotnet-api-docs\".";

    [McpServerTool(Name = "lookup_api", ReadOnly = true, Idempotent = true)]
    [Description(
        "Look up a .NET or Roslyn API type or member in synchronized ECMA XML docs. " +
        "TypeName returns every member's signature only; TypeName.MemberName returns full " +
        "documentation for that member. Each match reports detail - \"signatures\" or \"full\" - " +
        "because the tier is decided per source, so one source resolving the string as a type " +
        "never collapses another's member match. Pass source to restrict the lookup to " +
        "dotnet-api-docs or roslyn-api-docs, and limit/cursor to page. Returns provenance with " +
        "every match. " + FrameworkDescription)]
    public static async Task<string> LookupApi(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null,
        string? framework = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.LookupAsync(
                symbol,
                source,
                framework,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            // Outcome, not the page. A cursor landing exactly at the end of the result set yields
            // an empty page for a symbol that plainly exists, and reporting that as not_found sends
            // the caller to search_api, which will confirm the type and contradict the error.
            if (result.Outcome != ApiLookupOutcome.Found)
            {
                // A resolved type with no matching member is answerable with another lookup, and a
                // name that resolved to nothing is not answerable at all: search_api searches the
                // same coverage this lookup already reports, so sending the caller there would
                // promise a completeness neither tool has.
                var memberMissing = result.Outcome == ApiLookupOutcome.MemberNotFound;
                return JsonSerializer.Serialize(
                    new
                    {
                        error = memberMissing ? "member_not_found" : "not_found",
                        message = memberMissing
                            ? $"No member of '{string.Join("', '", result.ResolvedTypeNames)}' matches "
                                + $"'{symbol}'. Call lookup_api with just the type name to list its members."
                            : $"API symbol '{symbol}' was not found in the stated synchronized coverage; "
                                + "the name may be invalid or its package may be outside that coverage.",
                        symbol,
                        resolvedTypes = memberMissing ? result.ResolvedTypeNames : null,
                        searchedSources = result.SearchedSources,
                        effectiveFramework = result.EffectiveFramework,
                        defaultFramework = result.DefaultFramework,
                        availableFrameworks = result.AvailableFrameworks,
                    },
                    WriteOptions);
            }

            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (SourceNotSyncedException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_not_synced",
                    message = exception.Message,
                    source = exception.SourceName,
                },
                WriteOptions);
        }
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (FrameworkNotAvailableException exception)
        {
            return SerializeFrameworkNotAvailable(exception);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal)
                        ? "invalid_cursor"
                        : "invalid_request",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (InvalidDataException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_invalid",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    [McpServerTool(Name = "search_api", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search synchronized .NET and Roslyn ECMA XML docs by name. The pattern matches a " +
        "fully-qualified name (\"System.Text.Json.JsonSerializer\"), a whole namespace or a run of " +
        "its dot-separated segments from anywhere in the path (\"System.Text.Json\", \"Text.Json\", " +
        "\"Json\"), or a fragment of a type name (\"Concurrent\"). Namespaces match on complete " +
        "segments, type names on any substring. Every item reports matchedOn - \"fullName\", " +
        "\"type\" or \"namespace\" - so a namespace's entire contents is distinguishable from types " +
        "named for the pattern. A namespace match also reports namespaceDepth: 0 for a type " +
        "declared in that namespace itself, 1 for one a namespace below, and so on. A namespace " +
        "pattern always returns descendants too, so filter the page on namespaceDepth == 0 when " +
        "you want only what the namespace itself declares. " +
        "Returns fully-qualified candidate names only, with provenance and explicit pagination; " +
        "call lookup_api for documentation bodies. This tool matches type names, not members: a " +
        "dotted pattern that matched nothing carries a note pointing at lookup_api for a Type.Member " +
        "name, or at searching the simple name for a generic type's full name. " +
        FrameworkDescription)]
    public static async Task<string> SearchApi(
        string pattern,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? framework = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchAsync(
                pattern,
                framework,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (SourceNotSyncedException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_not_synced",
                    message = exception.Message,
                    source = exception.SourceName,
                },
                WriteOptions);
        }
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (FrameworkNotAvailableException exception)
        {
            return SerializeFrameworkNotAvailable(exception);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal)
                        ? "invalid_cursor"
                        : "invalid_request",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (InvalidDataException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_invalid",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    [McpServerTool(Name = "search_api_text", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search the PROSE inside synchronized .NET and Roslyn ECMA XML docs - summaries, remarks, " +
        "returns, parameter and exception descriptions - by literal case-insensitive substring. " +
        "This is the tool for \"which API mentions this behavior?\", the question lookup_api cannot " +
        "answer because it takes the name as input; use search_api when you have a name. " +
        "Returns the owning symbol, which documentation element matched, and the matched text " +
        "capped at 300 characters with isTruncated saying so - never whole documents. Feed the " +
        "returned symbol to lookup_api for the full entry. Hits are ranked (summaries first, " +
        "whole-word matches above mid-word ones), and at most two per symbol are returned; when a " +
        "symbol had more, its last kept hit reports moreFromSymbol and lookup_api on that symbol " +
        "reaches them. Regex is not supported here. " + FrameworkDescription)]
    public static async Task<string> SearchApiText(
        string query,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null,
        string? framework = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchTextAsync(
                query,
                source,
                framework,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (SourceNotSyncedException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_not_synced",
                    message = exception.Message,
                    source = exception.SourceName,
                },
                WriteOptions);
        }
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (FrameworkNotAvailableException exception)
        {
            return SerializeFrameworkNotAvailable(exception);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal)
                        ? "invalid_cursor"
                        : "invalid_request",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (InvalidDataException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_invalid",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    [McpServerTool(Name = "find_api_references", ReadOnly = true, Idempotent = true)]
    [Description(
        "Find declarations that USE a type: methods taking it as a parameter, methods returning it, " +
        "types deriving from it, types implementing it, type parameters constrained to it, and " +
        "declarations decorated with it. The inverse of lookup_api, which says " +
        "what a type offers rather than what uses it. Pass a fully-qualified type name. " +
        "Matches the type inside a compound signature too, so System.String finds string[], " +
        "out string and IEnumerable<string>. Each hit reports kind - \"parameter\", \"return\", " +
        "\"base\", \"interface\", \"constraint\" or \"attribute\" - plus the owning symbol, the " +
        "type expression and the C# " +
        "signature; kind also filters. kind says WHERE the reference sits, not that the type is " +
        "itself the base or interface: a class implementing IComparer<string> is an \"interface\" " +
        "hit for System.String. isExact says which - true when the declaration names the type " +
        "itself, false when it names an expression parameterized by it - and exact filters on it, " +
        "so \"what derives from Stream\" and \"what has a base parameterized by Stream\" are " +
        "separate queries. For an \"attribute\" hit typeExpression is the whole application text " +
        "and isExact separates \"decorated with this attribute\" from \"this type named inside " +
        "its arguments\"; for a \"constraint\" hit parameterName is the constrained type " +
        "parameter. " +
        "PASS THE ATTRIBUTE'S CLR NAME: the docs record an application the way C# spells it, with " +
        "the Attribute suffix elided, so the suffix is resolved inside the application and " +
        "System.ObsoleteAttribute is what finds [System.Obsolete(\"…\")]. An \"attribute\" hit " +
        "reports attributeType, the CLR name it resolved to, beside the source spelling in " +
        "typeExpression. The rule applies to applications ONLY: querying Foo never returns " +
        "FooAttribute's applications, because outside an application Foo means the class. Where " +
        "both types exist the response carries note - siblingType, how many applications were " +
        "excluded, and the call that reaches them - so the exclusion is never a silent zero. " +
        "Every response carries per-kind totals for the WHOLE result set, so a widely-used type is " +
        "visibly widely used rather than silently paginated. " +
        "Prose mentions are search_api_text's job, not this tool's. " + FrameworkDescription)]
    public static async Task<string> FindApiReferences(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? kind = null,
        bool? exact = null,
        string? source = null,
        string? framework = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.FindReferencesAsync(
                symbol,
                kind,
                exact,
                source,
                framework,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (SourceNotSyncedException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_not_synced",
                    message = exception.Message,
                    source = exception.SourceName,
                },
                WriteOptions);
        }
        catch (TimeoutException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "git_timeout",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (FrameworkNotAvailableException exception)
        {
            return SerializeFrameworkNotAvailable(exception);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal)
                        ? "invalid_cursor"
                        : "invalid_request",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (InvalidDataException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_invalid",
                    message = exception.Message,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    // The frameworks are data the caller cannot guess and cannot discover from the failure
    // otherwise, so the remedy travels as fields rather than as prose inside the message.
    private static string SerializeFrameworkNotAvailable(FrameworkNotAvailableException exception) =>
        JsonSerializer.Serialize(
            new
            {
                error = "framework_not_available",
                message = exception.Message,
                requestedFramework = exception.RequestedFramework,
                defaultFramework = exception.DefaultFramework,
                availableFrameworks = exception.AvailableFrameworks,
            },
            WriteOptions);

    private static string SerializeSourceInvalid(Exception exception) =>
        JsonSerializer.Serialize(
            new
            {
                error = "source_invalid",
                message = exception.Message,
            },
            WriteOptions);
}
