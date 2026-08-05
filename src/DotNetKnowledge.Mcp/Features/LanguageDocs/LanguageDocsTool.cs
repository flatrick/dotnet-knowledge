using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

[McpServerToolType]
public sealed class LanguageDocsTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "search_language_docs", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search synchronized C# and VB.NET language-design documents (proposals, spec, LDM " +
        "meeting notes) by literal substring or, with regex: true, a .NET regex evaluated with " +
        "the non-backtracking engine. Returns path:line hits with the matched line and a " +
        "server-issued section heading path, never file bodies; call get_language_doc for content.")]
    public static async Task<string> SearchLanguageDocs(
        string query,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        bool? regex = null,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchAsync(
                query,
                regex ?? false,
                source,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (RegexParseException exception)
        {
            return SerializeError("invalid_regex", exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return SerializeError("invalid_regex", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }

    [McpServerTool(Name = "get_language_doc", ReadOnly = true, Idempotent = true)]
    [Description(
        "Fetch a synchronized C# or VB.NET language-design document by its repo-relative path. " +
        "Pass section as a heading path exactly as returned by search_language_docs or " +
        "get_language_doc_outline to fetch just that section; omit it for the whole document. " +
        "Pages by an approximate character budget (limit) that never splits a fenced code block " +
        "or a table.")]
    public static async Task<string> GetLanguageDoc(
        string path,
        string source,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        string? section = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.GetDocAsync(
                path,
                source,
                section,
                limit ?? 8000,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (LanguageDocSectionNotFoundException exception)
        {
            return SerializeError("section_not_found", exception.Message);
        }
        catch (LanguageDocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }

    [McpServerTool(Name = "get_language_doc_outline", ReadOnly = true, Idempotent = true)]
    [Description(
        "Return a synchronized C# or VB.NET language-design document's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_language_doc's section " +
        "parameter accepts verbatim. Paginated like the other tools.")]
    public static async Task<string> GetLanguageDocOutline(
        string path,
        string source,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.GetOutlineAsync(
                path,
                source,
                limit ?? 100,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (LanguageDocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }

    private static string SerializeSourceNotSynced(SourceNotSyncedException exception) =>
        JsonSerializer.Serialize(
            new { error = "source_not_synced", message = exception.Message, source = exception.SourceName },
            WriteOptions);

    private static string SerializeArgumentException(ArgumentException exception) =>
        SerializeError(
            string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal) ? "invalid_cursor" : "invalid_request",
            exception.Message);

    private static string SerializeError(string error, string message) =>
        JsonSerializer.Serialize(new { error, message }, WriteOptions);
}
