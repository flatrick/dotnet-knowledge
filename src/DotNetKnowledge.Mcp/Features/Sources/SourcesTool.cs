using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetKnowledge.Mcp.Sources;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.Sources;

[McpServerToolType]
public sealed class SourcesTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
    public static async Task<string> ListSources(
        SourceCatalog catalog,
        SourceCache cache,
        SourceSynchronizer synchronizer,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceDefinitions = catalog.Sources
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();
            var sources = await Task.WhenAll(sourceDefinitions.Select(entry => GetSourceStatusAsync(
                entry.Key,
                entry.Value,
                cache,
                synchronizer,
                cancellationToken))).ConfigureAwait(false);

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
    }

    private static async Task<SourceStatus> GetSourceStatusAsync(
        string name,
        SourceDefinition definition,
        SourceCache cache,
        SourceSynchronizer synchronizer,
        CancellationToken cancellationToken)
    {
        SourceSyncState? state;
        try
        {
            state = await synchronizer.TryGetCurrentStateAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{name}: {exception.Message}", exception);
        }

        return new SourceStatus(
            Name: name,
            Repository: definition.Repository,
            Purpose: definition.Purpose,
            Url: definition.Url,
            Pin: definition.Pin,
            HeadBranch: definition.Head,
            Synced: state is not null,
            CurrentRef: state?.Ref,
            CurrentCommit: state?.Commit,
            FetchedAt: state?.FetchedAt,
            CacheDir: cache.DirectoryFor(name));
    }

    [McpServerTool(Name = "sync_source", Destructive = true, Idempotent = true)]
    [Description(
        "Clone or update one configured upstream reference source in the per-user cache. " +
        "Omit ref to fetch the pinned commit this server vouches for; pass ref=\"head\" to opt " +
        "into the configured upstream branch. Query tools never call this implicitly.")]
    public static async Task<string> SyncSource(
        string name,
        SourceSynchronizer synchronizer,
        CancellationToken cancellationToken,
        string? @ref = null)
    {
        try
        {
            var result = await synchronizer.SyncAsync(name, @ref, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(
                new
                {
                    name = result.Name,
                    cacheDir = result.CacheDir,
                    source = new
                    {
                        repo = result.Repository,
                        @ref = result.Ref,
                        commit = result.Commit,
                        fetchedAt = result.FetchedAt,
                    },
                },
                WriteOptions);
        }
        catch (ArgumentException exception) when (string.Equals(exception.ParamName, "name", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "unknown_source",
                    message = exception.Message,
                    source = name,
                },
                WriteOptions);
        }
        catch (ArgumentException exception) when (string.Equals(exception.ParamName, "requestedRef", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "invalid_ref",
                    message = exception.Message,
                    source = name,
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
                    source = name,
                },
                WriteOptions);
        }
        catch (InvalidOperationException exception)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "sync_failed",
                    message = exception.Message,
                    source = name,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "sync_failed",
                    message = exception.Message,
                    source = name,
                },
                WriteOptions);
        }
    }

    private sealed record SourceStatus(
        string Name,
        string Repository,
        string Purpose,
        string Url,
        string Pin,
        string HeadBranch,
        bool Synced,
        string? CurrentRef,
        string? CurrentCommit,
        DateTimeOffset? FetchedAt,
        string CacheDir);

    private sealed record ListSourcesResult(
        string CacheRoot,
        IReadOnlyList<SourceStatus> Sources,
        string? NextStep);
}
