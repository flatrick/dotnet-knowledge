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

    [McpServerTool(Name = "lookup_api", ReadOnly = true, Idempotent = true)]
    [Description(
        "Look up a .NET or Roslyn API type or member in synchronized ECMA XML docs. " +
        "TypeName returns every member's signature only; TypeName.MemberName returns full " +
        "documentation for that member. Pass source to restrict the lookup to dotnet-api-docs or " +
        "roslyn-api-docs, and limit/cursor to page. Returns provenance with every match.")]
    public static async Task<string> LookupApi(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.LookupAsync(
                symbol,
                source,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            // Outcome, not the page. A cursor landing exactly at the end of the result set yields
            // an empty page for a symbol that plainly exists, and reporting that as not_found sends
            // the caller to search_api, which will confirm the type and contradict the error.
            if (result.Outcome != ApiLookupOutcome.Found)
            {
                // Directing a caller to search_api is right when the type was not found and wrong
                // when the type resolved: search_api enumerates file names and never opens a
                // document, so no search of it can surface a member.
                var memberMissing = result.Outcome == ApiLookupOutcome.MemberNotFound;
                return JsonSerializer.Serialize(
                    new
                    {
                        error = memberMissing ? "member_not_found" : "not_found",
                        message = memberMissing
                            ? $"No member of '{string.Join("', '", result.ResolvedTypeNames)}' matches "
                                + $"'{symbol}'. Call lookup_api with just the type name to list its members."
                            : $"API symbol '{symbol}' was not found in the selected synchronized source(s). "
                                + "Call search_api with a type-name fragment to find candidates.",
                        symbol,
                        resolvedTypes = memberMissing ? result.ResolvedTypeNames : null,
                        searchedSources = result.SearchedSources,
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
        "named for the pattern. " +
        "Returns fully-qualified candidate names only, with provenance and explicit pagination; " +
        "call lookup_api for documentation bodies.")]
    public static async Task<string> SearchApi(
        string pattern,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchAsync(
                pattern,
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
        "returned symbol to lookup_api for the full entry. Regex is not supported here.")]
    public static async Task<string> SearchApiText(
        string query,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchTextAsync(
                query,
                source,
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
        "types deriving from it, and types implementing it. The inverse of lookup_api, which says " +
        "what a type offers rather than what uses it. Pass a fully-qualified type name. " +
        "Matches the type inside a compound signature too, so System.String finds string[], " +
        "out string and IEnumerable<string>. Each hit reports kind - \"parameter\", \"return\", " +
        "\"base\" or \"interface\" - plus the owning symbol, the type expression and the C# " +
        "signature; kind also filters. kind says WHERE the reference sits, not that the type is " +
        "itself the base or interface: a class implementing IComparer<string> is an \"interface\" " +
        "hit for System.String, and typeExpression says which. Compare typeExpression against the " +
        "symbol to tell an exact base or interface from a parameterized one. " +
        "Every response carries per-kind totals for the WHOLE result set, so a widely-used type is " +
        "visibly widely used rather than silently paginated. " +
        "Prose mentions are search_api_text's job, not this tool's.")]
    public static async Task<string> FindApiReferences(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? kind = null,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.FindReferencesAsync(
                symbol,
                kind,
                source,
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

    private static string SerializeSourceInvalid(Exception exception) =>
        JsonSerializer.Serialize(
            new
            {
                error = "source_invalid",
                message = exception.Message,
            },
            WriteOptions);
}
