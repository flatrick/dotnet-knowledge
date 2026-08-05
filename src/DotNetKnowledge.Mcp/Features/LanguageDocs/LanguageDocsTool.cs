using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
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
