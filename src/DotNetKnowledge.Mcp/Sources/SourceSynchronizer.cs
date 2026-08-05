using System.Collections.Concurrent;

namespace DotNetKnowledge.Mcp.Sources;

public sealed class SourceSynchronizer
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SourceLocks =
        new(StringComparer.Ordinal);

    private readonly SourceCatalog _catalog;
    private readonly SourceCache _cache;

    public SourceSynchronizer(SourceCatalog catalog, SourceCache cache)
    {
        _catalog = catalog;
        _cache = cache;
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
        CancellationToken cancellationToken)
    {
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        return await TryGetCurrentStateCoreAsync(name, definition, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> ReadCurrentSourceAsync<T>(
        string name,
        Func<SourceDefinition, SourceSyncState, string, T> reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!_catalog.TryGet(name, out var definition))
            throw new ArgumentException($"Unknown source '{name}'. Call list_sources to see valid names.", nameof(name));

        await using var sourceLock = await AcquireLockAsync(name, cancellationToken).ConfigureAwait(false);
        var state = await TryGetCurrentStateCoreAsync(name, definition, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Source '{name}' is not in a valid synchronized state.");
        return reader(definition, state, _cache.DirectoryFor(name));
    }

    private async Task<SourceSyncState?> TryGetCurrentStateCoreAsync(
        string name,
        SourceDefinition definition,
        CancellationToken cancellationToken)
    {
        var state = _cache.TryReadState(name);
        if (state is null
            || state.SchemaVersion != 1
            || !string.Equals(state.Name, name, StringComparison.Ordinal)
            || !string.Equals(state.Repository, definition.Repository, StringComparison.Ordinal)
            || !string.Equals(state.Url, definition.Url, StringComparison.Ordinal)
            || !state.SparsePaths.SequenceEqual(definition.Sparse, StringComparer.Ordinal)
            || !IsConfiguredRef(state, definition))
        {
            return null;
        }

        var directory = _cache.DirectoryFor(name);
        if (!Directory.Exists(Path.Combine(directory, ".git"))
            && !File.Exists(Path.Combine(directory, ".git")))
        {
            return null;
        }

        try
        {
            var actualCommit = (await GitCommandRunner.RunAsync(
                directory,
                ["rev-parse", "HEAD"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false)).Trim();
            var origin = (await GitCommandRunner.RunAsync(
                directory,
                ["config", "--get", "remote.origin.url"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false)).Trim();
            var status = await GitCommandRunner.RunAsync(
                directory,
                ["status", "--porcelain", "--untracked-files=all"],
                GitCommandKind.Quick,
                cancellationToken).ConfigureAwait(false);
            var sparseRootsExist = definition.Sparse.All(path =>
                Directory.Exists(Path.Combine(directory, path))
                || File.Exists(Path.Combine(directory, path)));
            return string.Equals(actualCommit, state.Commit, StringComparison.OrdinalIgnoreCase)
                && OriginsMatch(origin, definition.Url)
                && string.IsNullOrWhiteSpace(status)
                && sparseRootsExist
                    ? state
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
        var destination = _cache.DirectoryFor(name);
        Directory.CreateDirectory(_cache.Root);
        var staging = Path.Combine(_cache.Root, $".{name}-{Guid.NewGuid():N}.tmp");
        var repositoryDirectory = destination;
        Exception? primaryException = null;

        try
        {
            progress?.Report("clone");
            await GitCommandRunner.RunAsync(
                null,
                ["clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", "--", definition.Url, staging],
                GitCommandKind.Bulk,
                cancellationToken).ConfigureAwait(false);
            repositoryDirectory = staging;

            var commit = await SynchronizeRepositoryAsync(
                repositoryDirectory,
                definition,
                target,
                progress,
                cancellationToken).ConfigureAwait(false);
            var fetchedAt = DateTimeOffset.UtcNow;

            var statePath = _cache.StatePathFor(name);
            if (File.Exists(statePath))
                File.Delete(statePath);
            if (Directory.Exists(destination))
                DeleteDirectory(destination);
            Directory.Move(staging, destination);
            _cache.WriteState(
                name,
                new SourceSyncState(
                    SchemaVersion: 1,
                    Name: name,
                    Repository: definition.Repository,
                    Url: definition.Url,
                    Ref: refLabel,
                    Commit: commit,
                    FetchedAt: fetchedAt,
                    SparsePaths: definition.Sparse));

            return new SourceSyncResult(
                Name: name,
                Repository: definition.Repository,
                Ref: refLabel,
                Commit: commit,
                FetchedAt: fetchedAt,
                CacheDir: destination);
        }
        catch (Exception exception)
        {
            primaryException = exception;
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try
                {
                    DeleteDirectory(staging);
                }
                catch when (primaryException is not null)
                {
                    // Preserve the synchronization or cancellation failure that caused cleanup.
                }
            }
        }
    }

    private static async Task<string> SynchronizeRepositoryAsync(
        string repositoryDirectory,
        SourceDefinition definition,
        string target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("sparse-checkout");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["sparse-checkout", "set", .. definition.Sparse],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);
        progress?.Report("fetch");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["fetch", "--depth", "1", "--quiet", "origin", target],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);
        progress?.Report("checkout");
        await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["checkout", "--detach", "--quiet", "FETCH_HEAD"],
            GitCommandKind.Bulk,
            cancellationToken).ConfigureAwait(false);

        progress?.Report("validate");
        var commit = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["rev-parse", "HEAD"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false)).Trim();
        var origin = (await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["config", "--get", "remote.origin.url"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false)).Trim();
        var status = await GitCommandRunner.RunAsync(
            repositoryDirectory,
            ["status", "--porcelain", "--untracked-files=all"],
            GitCommandKind.Quick,
            cancellationToken).ConfigureAwait(false);
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
