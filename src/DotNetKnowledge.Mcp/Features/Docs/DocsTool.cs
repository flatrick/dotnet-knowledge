using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Yaml;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.Docs;

[McpServerToolType]
public sealed class DocsTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "search_docs", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search synchronized documentation sources - C# and VB.NET language design (proposals, " +
        "spec, LDM meeting notes), Roslyn contributor docs, and NuGet package management - by " +
        "literal substring or, with regex: true, a .NET regex evaluated with the non-backtracking " +
        "engine. Returns path:line hits with the matched line and a server-issued section heading " +
        "path, never file bodies; call get_doc for content. A long matched line is capped at 300 " +
        "characters with isTruncated saying so; the text carries no marker, so any ellipsis in it " +
        "is the source's own. Fetch the document for the full text. YAML front matter, which " +
        "Microsoft Learn articles carry, is metadata about a document rather than part of it and " +
        "is not searched. A literal, non-regex query that matches nothing is retried once against " +
        "an HTML-entity/typography-decoded form; a hit set produced this way carries " +
        "normalizationNote naming the form actually matched. " +
        "A hit carrying renderedFrom came from a document this server rendered rather than read " +
        "verbatim - today a Microsoft Learn structured FAQ (renderedFrom \"YamlMime:FAQ\"). Its " +
        "path names a real file, but the line number indexes the rendering, not the file's bytes. " +
        "A document that declared a schema this server renders and then could not be read is " +
        "named in skippedDocuments with the reason and its source's provenance, rather than " +
        "silently contributing no hits.")]
    public static async Task<string> SearchDocs(
        string query,
        DocsQueryService service,
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    [McpServerTool(Name = "get_doc", ReadOnly = true, Idempotent = true)]
    [Description(
        "Fetch a synchronized documentation file by its repo-relative path. " +
        "Pass section as a heading path exactly as returned by search_docs or " +
        "get_doc_outline to fetch just that section; omit it for the whole document. " +
        "Heading paths are normalized - inline markdown such as backticks is stripped, and levels " +
        "are joined with \" > \" - so build them from those tools rather than from raw markdown, " +
        "where \"## `Span<char>` support\" reads as \"Span<char> support\". " +
        "Pages by an approximate character budget (limit) that never splits a fenced code block " +
        "or a table. Text is returned exactly as authored: Microsoft Learn syntax such as " +
        "[!INCLUDE [x](../includes/x.md)], > [!NOTE] alerts and :::image blocks is not resolved, " +
        "and an include token names a real path this tool can fetch. A whole-document fetch begins " +
        "at the document's first content line: YAML front matter is metadata and is not returned, " +
        "and startLine names the line the text actually came from. If \"path\" or \"section\" " +
        "doesn't match exactly, one retry is attempted against an HTML-entity/typography-decoded " +
        "form of the same value; a response produced this way carries normalizationNote and " +
        "reports the resolved path/section, never the request's own spelling. " +
        "A response carrying renderedFrom is the server's rendering of a structured document - " +
        "today a Microsoft Learn FAQ, whose sections and questions become headings - so startLine " +
        "and endLine index that rendering rather than the file on disk. The FAQ's own title and " +
        "metadata are identity rather than content and are not returned.")]
    public static async Task<string> GetDoc(
        string path,
        string source,
        DocsQueryService service,
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
        catch (DocSectionNotFoundException exception)
        {
            return SerializeError("section_not_found", exception.Message);
        }
        catch (DocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (FaqParseException exception)
        {
            return SerializeError("document_unreadable", exception.Message);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    [McpServerTool(Name = "get_doc_outline", ReadOnly = true, Idempotent = true)]
    [Description(
        "Return a synchronized documentation file's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_doc's section " +
        "parameter accepts verbatim. YAML front matter, which Microsoft Learn articles carry, is " +
        "not a heading and does not appear. Paginated like the other tools. If \"path\" doesn't " +
        "match exactly, one retry is attempted against an HTML-entity/typography-decoded form; a " +
        "response produced this way carries normalizationNote and reports the resolved path. " +
        "A Microsoft Learn structured FAQ has a heading tree even though the file is YAML: its " +
        "sections are level 1 and its questions level 2, and the response carries renderedFrom.")]
    public static async Task<string> GetDocOutline(
        string path,
        string source,
        DocsQueryService service,
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
        catch (DocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (FaqParseException exception)
        {
            return SerializeError("document_unreadable", exception.Message);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SerializeSourceInvalid(exception);
        }
    }

    private static string SerializeSourceInvalid(Exception exception) =>
        SerializeError("source_invalid", exception.Message);

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
