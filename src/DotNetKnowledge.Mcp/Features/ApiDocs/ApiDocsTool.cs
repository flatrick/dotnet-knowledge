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
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "lookup_api", ReadOnly = true, Idempotent = true)]
    [Description(
        "Look up a .NET or Roslyn API type or member in synchronized ECMA XML docs. " +
        "Use TypeName or TypeName.MemberName; pass source to restrict the lookup to " +
        "dotnet-api-docs or roslyn-api-docs. Returns signatures and documentation with provenance.")]
    public static async Task<string> LookupApi(
        string symbol,
        ApiDocsQueryService service,
        CancellationToken cancellationToken,
        string? source = null)
    {
        try
        {
            var result = await service.LookupAsync(symbol, source, cancellationToken).ConfigureAwait(false);
            if (result.Matches.Count == 0)
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
                    error = "invalid_request",
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
        "Search synchronized .NET and Roslyn ECMA XML docs by type-name fragment. " +
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

    private static string SerializeSourceInvalid(Exception exception) =>
        JsonSerializer.Serialize(
            new
            {
                error = "source_invalid",
                message = exception.Message,
            },
            WriteOptions);
}
