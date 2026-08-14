using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetKnowledge.Mcp.Sources;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.Sources;

[McpServerToolType]
public sealed class SourcesTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        // Indentation is roughly a fifth of every response's bytes and buys an agent nothing.
        WriteIndented = false,
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
                    cacheRoot = cache.Root,
                },
                WriteOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(
                new
                {
                    error = "source_invalid",
                    message = exception.Message,
                    cacheRoot = cache.Root,
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

        var supplements = MapSupplements(definition.ApiPackages, state?.ApiPackages, state?.Ref);
        return new SourceStatus(
            Name: name,
            Repository: definition.Repository,
            Purpose: definition.Purpose,
            Url: definition.Url,
            Pin: definition.Pin,
            HeadBranch: definition.Head,
            Synced: state is not null && supplements.All(supplement => supplement.Synced),
            CurrentRef: state?.Ref,
            CurrentCommit: state?.Commit,
            FetchedAt: state?.FetchedAt,
            CacheDir: state is null
                ? cache.DirectoryFor(name)
                : cache.RepositoryDirectoryFor(name, state.Generation),
            Supplements: supplements);
    }

    [McpServerTool(Name = "sync_source", Destructive = true, Idempotent = true)]
    [Description(
        "Clone or update one configured upstream reference source in the per-user cache. " +
        "Omit ref to fetch the pinned commit this server vouches for; pass ref=\"head\" to opt " +
        "into the configured upstream branch. Query tools never call this implicitly.")]
    public static async Task<string> SyncSource(
        string name,
        SourceSynchronizer synchronizer,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        string? @ref = null)
    {
        try
        {
            // The SDK supplies a no-op reporter when the client sent no progress token. Package
            // supplements add their three stages to the five-stage Git synchronization.
            var stageProgress = new StageReporter(name, synchronizer.GetStageCount(name), progress);
            var result = await synchronizer
                .SyncAsync(name, @ref, cancellationToken, stageProgress)
                .ConfigureAwait(false);
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
                    supplements = MapSupplements(result.ConfiguredApiPackages, result.ApiPackages, result.Ref),
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

    /// <summary>
    /// Reports stages inline rather than through <see cref="Progress{T}"/>, whose callbacks post to
    /// the thread pool when there is no synchronization context — as in this stdio host. That would
    /// make the running count a non-atomic read-modify-write and let notifications overtake the
    /// sequential stages they describe.
    /// </summary>
    private sealed class StageReporter(string sourceName, int totalStages, IProgress<ProgressNotificationValue> progress)
        : IProgress<string>
    {
        private int _completed;

        public void Report(string stage) =>
            progress.Report(new ProgressNotificationValue
            {
                Progress = ++_completed,
                Total = totalStages,
                Message = $"{sourceName}: {stage}",
            });
    }

    private static SupplementStatus[] MapSupplements(
        IReadOnlyList<ApiPackageDefinition>? definitions,
        IReadOnlyList<ApiPackageSyncState>? synchronizedPackages,
        string? synchronizedRef)
    {
        var states = synchronizedPackages ?? [];
        return (definitions ?? [])
            .Select(definition =>
            {
                var state = FindValidatedState(definition, states, synchronizedRef);
                return new SupplementStatus(
                    PackageId: definition.PackageId,
                    AssemblyName: state?.AssemblyName ?? definition.AssemblyName,
                    Feed: state?.Feed ?? definition.Feed,
                    Version: state?.Version ?? definition.Version,
                    Sha512: state?.Sha512 ?? definition.Sha512,
                    Synced: state is not null,
                    FetchedAt: state?.FetchedAt,
                    DefaultFramework: state?.DefaultFramework ?? definition.DefaultFramework,
                    AvailableFrameworks: state?.AvailableFrameworks ?? []);
            })
            .ToArray();
    }

    private static ApiPackageSyncState? FindValidatedState(
        ApiPackageDefinition definition,
        IReadOnlyList<ApiPackageSyncState> states,
        string? synchronizedRef)
    {
        var matches = states
            .Where(state => state is not null && string.Equals(
                state.PackageId,
                definition.PackageId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            return null;

        var state = matches[0];
        if (!IsStructurallyComplete(state)
            || !string.Equals(state.AssemblyName, definition.AssemblyName, StringComparison.Ordinal)
            || !string.Equals(state.Feed, definition.Feed, StringComparison.Ordinal)
            || !string.Equals(state.DefaultFramework, definition.DefaultFramework, StringComparison.OrdinalIgnoreCase)
            || !state.AvailableFrameworks.Contains(definition.DefaultFramework, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(synchronizedRef, "pinned", StringComparison.Ordinal)
            && (!string.Equals(state.Version, definition.Version, StringComparison.Ordinal)
                || !string.Equals(state.Sha512, definition.Sha512, StringComparison.Ordinal)))
        {
            return null;
        }

        return state;
    }

    private static bool IsStructurallyComplete(ApiPackageSyncState state) =>
        NuGetPackageIdentity.IsValidPackageId(state.PackageId)
        && !string.IsNullOrWhiteSpace(state.AssemblyName)
        && !state.AssemblyName.Contains('/')
        && !state.AssemblyName.Contains('\\')
        && !state.AssemblyName.Contains(':')
        && NuGetPackageIdentity.IsValidVersion(state.Version)
        && IsSha512(state.Sha512)
        && Uri.TryCreate(state.Feed, UriKind.Absolute, out var feed)
        && string.Equals(feed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && state.FetchedAt != default
        && PortableFrameworkName.IsSafe(state.DefaultFramework)
        && state.AvailableFrameworks is { Count: > 0 }
        && state.AvailableFrameworks.All(PortableFrameworkName.IsSafe)
        && IsRelativeCorpusPath(state.CorpusDirectory);

    private static bool IsSha512(string? sha512)
    {
        if (string.IsNullOrWhiteSpace(sha512))
            return false;

        try
        {
            return Convert.FromBase64String(sha512).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsRelativeCorpusPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Contains(':')
        && path.Split(['/', '\\'], StringSplitOptions.None).All(segment =>
            !string.IsNullOrWhiteSpace(segment)
            && segment is not "." and not "..");

    private sealed record SupplementStatus(
        string PackageId,
        string AssemblyName,
        string Feed,
        string Version,
        string Sha512,
        bool Synced,
        DateTimeOffset? FetchedAt,
        string DefaultFramework,
        IReadOnlyList<string> AvailableFrameworks);

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
        string CacheDir,
        IReadOnlyList<SupplementStatus> Supplements);

    private sealed record ListSourcesResult(
        string CacheRoot,
        IReadOnlyList<SourceStatus> Sources,
        string? NextStep);
}
