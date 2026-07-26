using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetKnowledge.Mcp.Sources;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.Sources;

[McpServerToolType]
internal sealed class SourcesTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "list_sources", ReadOnly = true, Idempotent = true)]
    [Description(
        "List the upstream reference sources this server can query, with the commit each one is " +
        "pinned to, whether it has been synced yet, and the on-disk cache directory. " +
        "Call this before any api or language-doc lookup to see what is available. " +
        "The returned cacheDir is a real path: text-search it directly when a structured lookup " +
        "does not cover what you need. " +
        "The bundled example corpus is not listed here — it ships with the server and needs no sync.")]
    public static string ListSources(SourceCatalog catalog, SourceCache cache)
    {
        var sources = catalog.Sources
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new SourceStatus(
                Name: entry.Key,
                Purpose: entry.Value.Purpose,
                Url: entry.Value.Url,
                Pin: entry.Value.Pin,
                HeadBranch: entry.Value.Head,
                Synced: cache.IsSynced(entry.Key),
                CacheDir: cache.DirectoryFor(entry.Key)))
            .ToList();

        var unsynced = sources.Where(source => !source.Synced).Select(source => source.Name).ToList();

        return JsonSerializer.Serialize(
            new ListSourcesResult(
                CacheRoot: cache.Root,
                Sources: sources,
                NextStep: unsynced.Count == 0
                    ? null
                    : $"Not synced: {string.Join(", ", unsynced)}. " +
                      $"Call sync_source(name: \"{unsynced[0]}\") before querying it."),
            WriteOptions);
    }

    private sealed record SourceStatus(
        string Name,
        string Purpose,
        string Url,
        string Pin,
        string HeadBranch,
        bool Synced,
        string CacheDir);

    private sealed record ListSourcesResult(
        string CacheRoot,
        IReadOnlyList<SourceStatus> Sources,
        string? NextStep);
}
