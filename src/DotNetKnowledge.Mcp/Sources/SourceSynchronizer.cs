using System.Collections.Concurrent;

namespace DotNetKnowledge.Mcp.Sources;

public sealed class SourceSynchronizer
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SourceLocks =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Applied to every staging repository before the checkout that populates it, because these
    /// caches are large: <c>dotnet-api-docs</c> indexes 42,531 paths. <c>feature.manyFiles</c> is
    /// the umbrella (<c>index.version=4</c>, <c>index.skipHash=true</c>, and the untracked cache);
    /// <c>core.untrackedCache</c> is then stated outright because it is the one that bears on
    /// <c>status --untracked-files=all</c>, and an umbrella's contents vary by git version.
    /// <para>
    /// <c>core.fsmonitor</c> is deliberately absent. It would help more than either of these, and
    /// it starts a long-lived <c>git fsmonitor--daemon</c> per repository that this server neither
    /// spawns nor supervises — the shape of the inherited-handle hang recorded in
    /// <c>docs/gotchas.md</c>, and a process that outlives the server that caused it.
    /// </para>
    /// </summary>
    private static readonly (string Key, string Value)[] LargeCheckoutSettings =
    [
        ("feature.manyFiles", "true"),
        ("core.untrackedCache", "true"),
    ];

    private readonly SourceCatalog _catalog;
    private readonly SourceCache _cache;
    private readonly IReadOnlyList<ISourceGenerationContributor> _contributors;
    private readonly GitTimeouts _timeouts;

    public SourceSynchronizer(SourceCatalog catalog, SourceCache cache)
        : this(catalog, cache, [], GitTimeouts.Default)
    {
    }

    public SourceSynchronizer(
        SourceCatalog catalog,
        SourceCache cache,
        IEnumerable<ISourceGenerationContributor> contributors)
        : this(catalog, cache, contributors, GitTimeouts.Default)
    {
    }

    /// <summary>Exists so tests can pin a tier to a ceiling that proves which one a command uses.</summary>
    internal SourceSynchronizer(SourceCatalog catalog, SourceCache cache, GitTimeouts timeouts)
        : this(catalog, cache, [], timeouts)
    {
    }

    internal SourceSynchronizer(
        SourceCatalog catalog,
        SourceCache cache,
        IEnumerable<ISourceGenerationContributor> contributors,
        GitTimeouts timeouts)
    {
        _catalog = catalog;
        _cache = cache;
        _contributors = contributors.ToList();
        _timeouts = timeouts;
    }

    public async Task<SourceSyncResult> SyncAsync(
        string name,
        string? requestedRef,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        if (requestedRef is not null && !string.Equals(requestedRef, "head", StringComparison.Ordinal))
            throw new ArgumentException("ref must be omitted or \"head\".", nameof(requestedRef));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        return await SyncCoreAsync(name, definition, requestedRef, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SourceSyncState?> TryGetCurrentStateAsync(
        string name,
        CancellationToken cancellationToken) =>
        (await TryGetCurrentSnapshotAsync(name, cancellationToken).ConfigureAwait(false))?.State;

    public async Task<SourceSnapshot?> TryGetCurrentSnapshotAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        return await TryGetCurrentSnapshotCoreAsync(name, definition, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> ReadCurrentSourceAsync<T>(
        string name,
        Func<SourceSnapshot, T> reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        var snapshot = await TryGetCurrentSnapshotCoreAsync(name, definition, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Source '{name}' is not in a valid synchronized state.");
        return reader(snapshot);
    }

    private async Task<SourceSnapshot?> TryGetCurrentSnapshotCoreAsync(
        string name,
        SourceDefinition definition,
        CancellationToken cancellationToken)
    {
        var state = _cache.TryReadState(name);
        if (state is null
            || state.SchemaVersion != 2
            || !string.Equals(state.Name, name, StringComparison.Ordinal)
            || !string.Equals(state.Repository, definition.Repository, StringComparison.Ordinal)
            || !string.Equals(state.Url, definition.Url, StringComparison.Ordinal)
            || !state.SparsePaths.SequenceEqual(definition.Sparse, StringComparer.Ordinal)
            || !IsConfiguredRef(state, definition))
        {
            return null;
        }

        var generationDirectory = _cache.GenerationDirectoryFor(name, state.Generation);
        var repositoryDirectory = _cache.RepositoryDirectoryFor(name, state.Generation);
        var supplementsDirectory = _cache.SupplementsDirectoryFor(name, state.Generation);
        if ((!Directory.Exists(Path.Combine(repositoryDirectory, ".git"))
             && !File.Exists(Path.Combine(repositoryDirectory, ".git")))
            || !Directory.Exists(supplementsDirectory))
        {
            return null;
        }

        try
        {
            var actualCommit = (await GitCommandRunner.RunAsync(
                repositoryDirectory,
                ["rev-parse", "HEAD"],
                GitCommandKind.Quick,
                cancellationToken,
                _timeouts).ConfigureAwait(false)).Trim();
            var origin = (await GitCommandRunner.RunAsync(
                repositoryDirectory,
                ["config", "--get", "remote.origin.url"],
                GitCommandKind.Quick,
                cancellationToken,
                _timeouts).ConfigureAwait(false)).Trim();
            var status = await GitCommandRunner.RunAsync(
                repositoryDirectory,
                ["status", "--porcelain", "--untracked-files=all"],
                GitCommandKind.Walk,
                cancellationToken,
                _timeouts).ConfigureAwait(false);
            var sparseRootsExist = definition.Sparse.All(path =>
                Directory.Exists(Path.Combine(repositoryDirectory, path))
                || File.Exists(Path.Combine(repositoryDirectory, path)));
            return string.Equals(actualCommit, state.Commit, StringComparison.OrdinalIgnoreCase)
                && OriginsMatch(origin, definition.Url)
                && string.IsNullOrWhiteSpace(status)
                && sparseRootsExist
                    ? new SourceSnapshot(
                        definition,
                        state,
                        generationDirectory,
                        repositoryDirectory,
                        supplementsDirectory)
                    : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<SourceSyncResult> SyncCoreAsync(
        string name,
        SourceDefinition definition,
        string? requestedRef,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var refLabel = requestedRef is null ? "pinned" : $"head:{definition.Head}";
        var target = requestedRef is null ? definition.Pin : definition.Head;
        Directory.CreateDirectory(_cache.Root);
        PruneGenerationDirectories(name, _cache.TryReadState(name)?.Generation);

        // A staging directory left behind by a failed attempt already holds the object store, so
        // resuming costs a fetch and a checkout instead of another full clone — 773 MB and about a
        // minute for dotnet-api-docs. Validation runs again either way, so a resumed tree is held
        // to exactly the same standard as a freshly cloned one.
        var resumed = await TryClaimResumableStagingAsync(name, definition, cancellationToken)
            .ConfigureAwait(false);
        var staging = resumed ?? Path.Combine(_cache.Root, $".{name}-{Guid.NewGuid():N}.tmp");
        var generation = Guid.NewGuid().ToString("N");
        var generationsDirectory = _cache.GenerationsDirectoryFor(name);
        var generationStaging = Path.Combine(generationsDirectory, $".{generation}.tmp");
        var generationDirectory = _cache.GenerationDirectoryFor(name, generation);
        var stagedRepositoryDirectory = Path.Combine(generationStaging, "repository");
        var stagedSupplementsDirectory = Path.Combine(generationStaging, "supplements");
        var repositoryValidated = false;
        var published = false;
        Exception? primaryException = null;

        try
        {
            Directory.CreateDirectory(generationStaging);
            Directory.CreateDirectory(stagedSupplementsDirectory);
            if (resumed is null)
            {
                progress?.Report("clone");
                await GitCommandRunner.RunAsync(
                    null,
                    ["clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", "--", definition.Url, stagedRepositoryDirectory],
                    GitCommandKind.Bulk,
                    cancellationToken,
                    _timeouts).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("resume");
                Directory.Move(staging, stagedRepositoryDirectory);
            }

            await ConfigureLargeCheckoutAsync(stagedRepositoryDirectory, cancellationToken).ConfigureAwait(false);

            var commit = await SynchronizeRepositoryAsync(
                stagedRepositoryDirectory,
                definition,
                target,
                progress,
                _timeouts,
                cancellationToken).ConfigureAwait(false);
            repositoryValidated = true;
            var fetchedAt = DateTimeOffset.UtcNow;

            var apiPackages = new List<ApiPackageSyncState>();
            foreach (var contributor in _contributors.Where(contributor => contributor.AppliesTo(definition)))
            {
                apiPackages.AddRange(await contributor.BuildAsync(
                    definition,
                    refLabel,
                    stagedRepositoryDirectory,
                    stagedSupplementsDirectory,
                    progress,
                    cancellationToken).ConfigureAwait(false));
            }

            Directory.Move(generationStaging, generationDirectory);
            _cache.WriteState(
                name,
                new SourceSyncState(
                    SchemaVersion: 2,
                    Name: name,
                    Repository: definition.Repository,
                    Url: definition.Url,
                    Ref: refLabel,
                    Commit: commit,
                    FetchedAt: fetchedAt,
                    SparsePaths: definition.Sparse,
                    Generation: generation,
                    ApiPackages: apiPackages));
            published = true;

            PruneGenerationDirectories(name, generation);

            var repositoryDirectory = _cache.RepositoryDirectoryFor(name, generation);

            return new SourceSyncResult(
                Name: name,
                Repository: definition.Repository,
                Ref: refLabel,
                Commit: commit,
                FetchedAt: fetchedAt,
                CacheDir: repositoryDirectory,
                ApiPackages: apiPackages);
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
        {
            if (primaryException is not null && !published)
                CleanupFailedPublication(
                    generationStaging,
                    generationDirectory,
                    generationsDirectory,
                    staging,
                    resumed is not null,
                    repositoryValidated,
                    DeleteDirectory);
        }
    }

    internal static void CleanupFailedPublication(
        string generationStaging,
        string generationDirectory,
        string generationsDirectory,
        string staging,
        bool resumed,
        bool repositoryValidated,
        Action<string> deleteDirectory)
    {
        try
        {
            var unpublishedDirectory = Directory.Exists(generationStaging)
                ? generationStaging
                : Directory.Exists(generationDirectory)
                    ? generationDirectory
                    : null;
            if (unpublishedDirectory is not null)
            {
                var unpublishedRepository = Path.Combine(unpublishedDirectory, "repository");
                if (Directory.Exists(unpublishedRepository) && !Directory.Exists(staging))
                    Directory.Move(unpublishedRepository, staging);
                deleteDirectory(unpublishedDirectory);
                if (!Directory.EnumerateFileSystemEntries(generationsDirectory).Any())
                    Directory.Delete(generationsDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the synchronization or cancellation failure that caused cleanup.
        }

        // A failed resume before Git validation makes the retained tree suspect. A contributor
        // failure happens after validation, so its repository remains resumable just like a
        // failed fresh synchronization. This attempt stays independent of generation cleanup.
        if (resumed && !repositoryValidated && Directory.Exists(staging))
        {
            try
            {
                deleteDirectory(staging);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Preserve the synchronization or cancellation failure that caused cleanup.
            }
        }
    }

    private void PruneGenerationDirectories(string name, string? currentGeneration) =>
        PruneGenerationDirectories(
            _cache.GenerationsDirectoryFor(name),
            currentGeneration,
            Directory.EnumerateDirectories);

    internal static void PruneGenerationDirectories(
        string generationsDirectory,
        string? currentGeneration,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        if (!Directory.Exists(generationsDirectory))
            return;

        IReadOnlyList<string> candidates;
        try
        {
            candidates = enumerateDirectories(generationsDirectory).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var currentDirectory = currentGeneration is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Path.Combine(generationsDirectory, currentGeneration)));
        foreach (var candidate in candidates)
        {
            var candidateDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (currentDirectory is not null
                && string.Equals(
                    candidateDirectory,
                    currentDirectory,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                DeleteDirectory(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Generation pruning is best-effort and never invalidates the current pointer.
            }
        }
    }

    /// <summary>
    /// Writes <see cref="LargeCheckoutSettings"/> into the staging repository. Runs before the
    /// checkout rather than after it, so the index that checkout writes is already the cheaper
    /// format. Idempotent, which is what lets it serve a fresh clone and a resumed tree alike.
    /// </summary>
    private async Task ConfigureLargeCheckoutAsync(
        string repositoryDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var (key, value) in LargeCheckoutSettings)
        {
            await GitCommandRunner.RunAsync(
                repositoryDirectory,
                ["config", key, value],
                GitCommandKind.Quick,
                cancellationToken,
                _timeouts).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns a staging directory from a previous failed attempt that is safe to continue from,
    /// removing every other candidate for this source. Runs under the per-source lock, so no
    /// concurrent sync of the same source can be holding one.
    /// </summary>
    private async Task<string?> TryClaimResumableStagingAsync(
        string name,
        SourceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_cache.Root))
            return null;

        string? claimed = null;
        foreach (var candidate in Directory
                     .EnumerateDirectories(_cache.Root, $".{name}-*.tmp")
                     .OrderByDescending(Directory.GetLastWriteTimeUtc)
                     .ToList())
        {
            if (claimed is null
                && await IsResumableAsync(candidate, definition, cancellationToken).ConfigureAwait(false))
            {
                claimed = candidate;
                continue;
            }

            try
            {
                DeleteDirectory(candidate);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An abandoned directory that will not delete is not a reason to fail the sync.
            }
        }

        return claimed;
    }

    /// <summary>
    /// A candidate is resumable only when it is a git repository whose origin is still the
    /// configured URL. A tree cloned from somewhere else would fail validation at the end of a long
    /// pipeline; rejecting it here costs one metadata read.
    /// </summary>
    private async Task<bool> IsResumableAsync(
        string candidate,
        SourceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(candidate, ".git")))
            return false;

        try
        {
            var origin = (await GitCommandRunner.RunAsync(
                candidate,
                ["config", "--get", "remote.origin.url"],
                GitCommandKind.Quick,
                cancellationToken,
                _timeouts).ConfigureAwait(false)).Trim();
            return OriginsMatch(origin, definition.Url);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            return false;
        }
    }

    private static async Task<string> SynchronizeRepositoryAsync(
        string repositoryDirectory,
        SourceDefinition definition,
        string target,
        IProgress<string>? progress,
        GitTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        progress?.Report("sparse-checkout");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["sparse-checkout", "set", .. definition.Sparse],
            GitCommandKind.Bulk,
            cancellationToken,
            timeouts).ConfigureAwait(false);
        progress?.Report("fetch");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["fetch", "--depth", "1", "--quiet", "origin", target],
            GitCommandKind.Bulk,
            cancellationToken,
            timeouts).ConfigureAwait(false);
        progress?.Report("checkout");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["checkout", "--detach", "--quiet", "FETCH_HEAD"],
            GitCommandKind.Bulk,
            cancellationToken,
            timeouts).ConfigureAwait(false);

        progress?.Report("validate");
        var commit = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["rev-parse", "HEAD"],
            GitCommandKind.Quick,
            cancellationToken,
            timeouts).ConfigureAwait(false)).Trim();
        var origin = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["config", "--get", "remote.origin.url"],
            GitCommandKind.Quick,
            cancellationToken,
            timeouts).ConfigureAwait(false)).Trim();
        var status = await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["status", "--porcelain", "--untracked-files=all"],
            GitCommandKind.Walk,
            cancellationToken,
            timeouts).ConfigureAwait(false);
        if (!OriginsMatch(origin, definition.Url)
            || !string.IsNullOrWhiteSpace(status)
            || definition.Sparse.Any(path =>
                !Directory.Exists(Path.Combine(repositoryDirectory, path))
                && !File.Exists(Path.Combine(repositoryDirectory, path))))
        {
            throw new InvalidOperationException("The synchronized checkout failed integrity validation.");
        }

        return commit;
    }

    private static bool IsConfiguredRef(SourceSyncState state, SourceDefinition definition) =>
        string.Equals(state.Ref, "pinned", StringComparison.Ordinal)
            ? string.Equals(state.Commit, definition.Pin, StringComparison.OrdinalIgnoreCase)
            : string.Equals(state.Ref, $"head:{definition.Head}", StringComparison.Ordinal);

    private async Task<SourceLock> AcquireLockAsync(string name, CancellationToken cancellationToken)
    {
        var key = Path.Combine(_cache.Root, name);
        var semaphore = SourceLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lockDirectory = Path.Combine(_cache.Root, ".locks");
            Directory.CreateDirectory(lockDirectory);
            var lockPath = Path.Combine(lockDirectory, name + ".lock");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.Asynchronous);
                    return new SourceLock(stream, semaphore);
                }
                catch (IOException)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    private sealed class SourceLock(FileStream stream, SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private static bool OriginsMatch(string actual, string configured)
    {
        if (Uri.TryCreate(actual, UriKind.Absolute, out var actualUri)
            && Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
            && !actualUri.IsFile
            && !configuredUri.IsFile)
        {
            return string.Equals(
                actualUri.AbsoluteUri.TrimEnd('/'),
                configuredUri.AbsoluteUri.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(actual),
                Path.GetFullPath(configured),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return string.Equals(actual.TrimEnd('/'), configured.TrimEnd('/'), StringComparison.Ordinal);
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }
}
