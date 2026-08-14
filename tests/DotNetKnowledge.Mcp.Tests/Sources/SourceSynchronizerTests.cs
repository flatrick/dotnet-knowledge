using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class SourceSynchronizerTests
{
    private static readonly string[] CompositeStages =
    [
        "clone", "sparse-checkout", "fetch", "checkout", "validate",
        "package-download", "package-validate", "package-normalize",
    ];
    private static readonly string[] GitStages =
        ["clone", "sparse-checkout", "fetch", "checkout", "validate"];

    [TestMethod]
    public async Task CompositeSyncPublishesPackageStateAfterGitValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var fixture = await CreatePackageFixtureAsync(root, ["net472", "net10.0"]);
            var contributor = new ApiPackageGenerationContributor(
                fixture.Client,
                new DotNetKnowledge.Mcp.Features.ApiDocs.Corpus.PackageApiCorpusBuilder());
            var synchronizer = new SourceSynchronizer(
                fixture.Catalog,
                fixture.Cache,
                [contributor],
                GitTimeouts.Default);
            var progress = new RecordingProgress();

            var result = await synchronizer.SyncAsync(
                "roslyn-api-docs",
                null,
                CancellationToken.None,
                progress);

            Assert.HasCount(1, result.ApiPackages);
            var package = result.ApiPackages[0];
            Assert.AreEqual("5.3.0", package.Version);
            Assert.AreEqual("net10.0", package.DefaultFramework);
            CollectionAssert.Contains(package.AvailableFrameworks.ToList(), "net472");
            CollectionAssert.AreEqual(CompositeStages, progress.Values.ToArray());
            Assert.AreEqual(8, synchronizer.GetStageCount("roslyn-api-docs"));
            Assert.AreEqual(0, Directory.EnumerateFiles(
                fixture.Cache.SupplementsDirectoryFor(
                    "roslyn-api-docs",
                    fixture.Cache.TryReadState("roslyn-api-docs")!.Generation),
                "*.nupkg",
                SearchOption.AllDirectories).Count());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("missing-default")]
    [DataRow("build")]
    [DataRow("cancel")]
    public async Task CompositeRefreshFailureRetainsThePublishedGeneration(string failure)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var fixture = await CreatePackageFixtureAsync(root, ["net472", "net10.0"]);
            var initialContributor = new ApiPackageGenerationContributor(
                fixture.Client,
                new DotNetKnowledge.Mcp.Features.ApiDocs.Corpus.PackageApiCorpusBuilder());
            var initialSynchronizer = new SourceSynchronizer(
                fixture.Catalog,
                fixture.Cache,
                [initialContributor],
                GitTimeouts.Default);
            await initialSynchronizer.SyncAsync("roslyn-api-docs", null, CancellationToken.None);
            var before = await initialSynchronizer.TryGetCurrentSnapshotAsync(
                "roslyn-api-docs",
                CancellationToken.None);
            Assert.IsNotNull(before);

            var retryPackage = Path.Combine(root, $"retry-{failure}.nupkg");
            PackageSyncFixture.CreatePackage(
                retryPackage,
                failure == "missing-default" ? ["net472"] : ["net10.0"],
                validAssembly: failure != "build");
            using var cancellation = new CancellationTokenSource();
            var retryClient = new FixtureNuGetPackageClient(retryPackage, fixture.ClientSha512)
            {
                Failure = failure == "hash" ? new InvalidDataException("fixture hash mismatch") : null,
                AfterCopy = failure == "cancel" ? cancellation.Cancel : null,
            };
            var retrySynchronizer = new SourceSynchronizer(
                fixture.Catalog,
                fixture.Cache,
                [new ApiPackageGenerationContributor(
                    retryClient,
                    new DotNetKnowledge.Mcp.Features.ApiDocs.Corpus.PackageApiCorpusBuilder())],
                GitTimeouts.Default);

            if (failure == "cancel")
            {
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => retrySynchronizer.SyncAsync(
                    "roslyn-api-docs", "head", cancellation.Token));
            }
            else
            {
                await Assert.ThrowsExactlyAsync<InvalidDataException>(() => retrySynchronizer.SyncAsync(
                    "roslyn-api-docs", "head", CancellationToken.None));
            }

            var after = await retrySynchronizer.TryGetCurrentSnapshotAsync(
                "roslyn-api-docs",
                CancellationToken.None);
            Assert.IsNotNull(after);
            Assert.AreEqual(before.State.Generation, after.State.Generation);
            Assert.AreEqual(before.State.Commit, after.State.Commit);
            Assert.AreEqual(
                JsonSerializer.Serialize(before.State.ApiPackages),
                JsonSerializer.Serialize(after.State.ApiPackages));
            Assert.AreEqual(
                0,
                Directory.EnumerateFiles(fixture.Cache.Root, "*.nupkg", SearchOption.AllDirectories).Count());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GitOnlySourceRetainsFiveStagesAndNoPackageState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var synchronizer = new SourceSynchronizer(
                catalog,
                cache,
                [new ApiPackageGenerationContributor(
                    new FixtureNuGetPackageClient(Path.Combine(root, "unused.nupkg"), Convert.ToBase64String(new byte[64])),
                    new DotNetKnowledge.Mcp.Features.ApiDocs.Corpus.PackageApiCorpusBuilder())],
                GitTimeouts.Default);

            var result = await synchronizer.SyncAsync("local", null, CancellationToken.None);

            Assert.AreEqual(5, synchronizer.GetStageCount("local"));
            Assert.HasCount(0, result.ApiPackages);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task NonRoslynSourceWithPackageDeclarationsRemainsGitOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = await CreateRepositoryAsync(root, "origin", "included");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteNonRoslynPackageCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var client = new FixtureNuGetPackageClient(
                Path.Combine(root, "must-not-download.nupkg"),
                Convert.ToBase64String(new byte[64]))
            {
                Failure = new InvalidOperationException("A non-Roslyn source attempted a package download."),
            };
            var synchronizer = new SourceSynchronizer(
                catalog,
                new SourceCache(Path.Combine(root, "cache")),
                [new ApiPackageGenerationContributor(
                    client,
                    new DotNetKnowledge.Mcp.Features.ApiDocs.Corpus.PackageApiCorpusBuilder())],
                GitTimeouts.Default);
            var progress = new RecordingProgress();

            var result = await synchronizer.SyncAsync("other", null, CancellationToken.None, progress);

            Assert.AreEqual(5, synchronizer.GetStageCount("other"));
            Assert.HasCount(0, result.ApiPackages);
            CollectionAssert.AreEqual(GitStages, progress.Values.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CancellationAfterContributorCompletionRetainsThePublishedGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var initial = new SourceSynchronizer(
                catalog,
                cache,
                [new RecordingContributor()],
                GitTimeouts.Default);
            await initial.SyncAsync("local", null, CancellationToken.None);
            var before = await initial.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(before);
            using var cancellation = new CancellationTokenSource();
            var retry = new SourceSynchronizer(
                catalog,
                cache,
                [new CancelingContributor(cancellation)],
                GitTimeouts.Default);

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                retry.SyncAsync("local", "head", cancellation.Token));

            var after = await retry.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(after);
            Assert.AreEqual(before.State.Generation, after.State.Generation);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FirstSyncPublishesACompleteGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var contributor = new RecordingContributor();
            var synchronizer = new SourceSynchronizer(
                catalog,
                cache,
                [contributor],
                GitTimeouts.Default);

            var result = await synchronizer.SyncAsync("local", null, CancellationToken.None);
            var snapshot = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual(snapshot.State.Generation, Path.GetFileName(snapshot.GenerationDirectory));
            Assert.AreEqual(cache.GenerationDirectoryFor("local", snapshot.State.Generation), snapshot.GenerationDirectory);
            Assert.AreEqual(Path.Combine(snapshot.GenerationDirectory, "repository"), snapshot.RepositoryDirectory);
            Assert.AreEqual(Path.Combine(snapshot.GenerationDirectory, "supplements"), snapshot.SupplementsDirectory);
            Assert.AreEqual(snapshot.RepositoryDirectory, result.CacheDir);
            Assert.IsTrue(File.Exists(Path.Combine(snapshot.RepositoryDirectory, "docs", "included.md")));
            Assert.IsTrue(File.Exists(Path.Combine(snapshot.SupplementsDirectory, "contributor.complete")));
            Assert.AreSequenceEqual(snapshot.State.ApiPackages, result.ApiPackages);
            Assert.IsFalse(Path.GetFileName(snapshot.GenerationDirectory).StartsWith('.'));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FailedContributorRetainsThePublishedGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var contributor = new RecordingContributor();
            var synchronizer = new SourceSynchronizer(
                catalog,
                cache,
                [contributor],
                GitTimeouts.Default);
            await synchronizer.SyncAsync("local", null, CancellationToken.None);
            var before = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(before);

            contributor.Failure = new InvalidOperationException("fixture failure");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => synchronizer.SyncAsync("local", "head", CancellationToken.None));
            var after = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);

            Assert.IsNotNull(after);
            Assert.AreEqual(before.State.Commit, after.State.Commit);
            Assert.AreEqual(before.GenerationDirectory, after.GenerationDirectory);
            Assert.IsTrue(Directory.Exists(after.GenerationDirectory));
            Assert.IsTrue(File.Exists(Path.Combine(after.SupplementsDirectory, "contributor.complete")));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ReaderWaitsUntilTheNewGenerationIsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var contributor = new RecordingContributor();
            var synchronizer = new SourceSynchronizer(
                catalog,
                cache,
                [contributor],
                GitTimeouts.Default);
            await synchronizer.SyncAsync("local", null, CancellationToken.None);

            contributor.PauseOnNextBuild();
            var sync = synchronizer.SyncAsync("local", "head", CancellationToken.None);
            await contributor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var read = synchronizer.ReadCurrentSourceAsync(
                "local",
                snapshot => (
                    Repository: File.ReadAllText(Path.Combine(snapshot.RepositoryDirectory, "docs", "included.md")),
                    SupplementComplete: File.Exists(Path.Combine(snapshot.SupplementsDirectory, "contributor.complete"))),
                CancellationToken.None);

            Assert.IsFalse(read.IsCompleted, "A reader entered the generation while its contributor was still running.");
            contributor.Continue.TrySetResult();
            await sync;
            var observed = await read;

            Assert.AreEqual("included", observed.Repository);
            Assert.IsTrue(observed.SupplementComplete);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public void GenerationPruningIgnoresDirectoryDiscoveryFailures()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "current"));

        SourceSynchronizer.PruneGenerationDirectories(
            root,
            "current",
            _ => throw new UnauthorizedAccessException("fixture failure"));

        Assert.IsTrue(Directory.Exists(Path.Combine(root, "current")));
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void GenerationPruningPreservesCurrentWindowsPathWhenPointerCasingDiffers()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows path comparison is case-insensitive.");

        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var generation = "abcdef0123456789";
        var generationDirectory = Path.Combine(root, generation);
        Directory.CreateDirectory(generationDirectory);

        try
        {
            SourceSynchronizer.PruneGenerationDirectories(
                root,
                generation.ToUpperInvariant(),
                Directory.EnumerateDirectories);

            Assert.IsTrue(Directory.Exists(generationDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FailedResumeDiscardsSuspectStagingEvenWhenGenerationCleanupFails()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var generationsDirectory = Path.Combine(root, ".generations", "local");
        var generationStaging = Path.Combine(generationsDirectory, ".generation.resume");
        var generationDirectory = Path.Combine(generationsDirectory, "generation");
        var repositoryDirectory = Path.Combine(generationStaging, "repository");
        Directory.CreateDirectory(repositoryDirectory);
        File.WriteAllText(Path.Combine(repositoryDirectory, "downloaded-object"), "suspect");

        try
        {
            SourceSynchronizer.CleanupFailedPublication(
                generationStaging,
                generationDirectory,
                generationsDirectory,
                resumed: true,
                repositoryValidated: false,
                _ => throw new IOException("fixture generation cleanup failure"));

            Assert.IsTrue(Directory.Exists(generationStaging));
            StringAssert.EndsWith(generationStaging, ".resume");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GenerationClaimRetriesOnlyTheFailedRenameUntilTheRepositoryIsMovable()
    {
        var attempts = 0;
        var delays = 0;

        await SourceSynchronizer.MoveDirectoryWhenReadyAsync(
            "source",
            "destination",
            TimeSpan.FromSeconds(1),
            CancellationToken.None,
            (source, destination) =>
            {
                Assert.AreEqual("source", source);
                Assert.AreEqual("destination", destination);
                attempts++;
                if (attempts == 1)
                    throw new IOException("fixture repository handle");
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, delays);
    }

    [TestMethod]
    public async Task GenerationClaimHonorsCancellationWhileWaitingForRepositoryReadiness()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            SourceSynchronizer.MoveDirectoryWhenReadyAsync(
                "source",
                "destination",
                TimeSpan.FromSeconds(1),
                cancellation.Token,
                (_, _) =>
                {
                    attempts++;
                    throw new IOException("fixture repository handle");
                },
                (_, _) =>
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task GenerationClaimReportsWhenRepositoryReadinessExceedsItsBound()
    {
        var delayCalled = false;

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            SourceSynchronizer.MoveDirectoryWhenReadyAsync(
                "source",
                "destination",
                TimeSpan.Zero,
                CancellationToken.None,
                (_, _) => throw new IOException("fixture repository handle"),
                (_, _) =>
                {
                    delayCalled = true;
                    return Task.CompletedTask;
                }));

        StringAssert.Contains(exception.Message, "repository did not become movable");
        Assert.IsFalse(delayCalled);
    }

    [TestMethod]
    public void FailedFreshSyncRetainsTheUnpublishedGenerationInPlace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var generationsDirectory = Path.Combine(root, ".generations", "local");
        var generationStaging = Path.Combine(generationsDirectory, ".generation.tmp");
        var generationDirectory = Path.Combine(generationsDirectory, "generation");
        var repositoryDirectory = Path.Combine(generationStaging, "repository");
        var oldStagingLocation = Path.Combine(root, ".local-download.tmp");
        Directory.CreateDirectory(repositoryDirectory);
        File.WriteAllText(Path.Combine(repositoryDirectory, "downloaded-object"), "resumable");

        try
        {
            SourceSynchronizer.CleanupFailedPublication(
                generationStaging,
                generationDirectory,
                generationsDirectory,
                resumed: false,
                repositoryValidated: false,
                DeleteDirectory);

            Assert.IsTrue(
                File.Exists(Path.Combine(repositoryDirectory, "downloaded-object")),
                "Failure cleanup moved the repository while the terminated Git process could "
                    + "still hold a Windows handle inside it.");
            Assert.IsFalse(
                Directory.Exists(oldStagingLocation),
                "A failed generation must remain recoverable in place until a later sync proves "
                    + "the repository is movable.");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task NextSyncPrunesAbandonedAndNoncurrentGenerations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("local", null, CancellationToken.None);
            var before = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(before);
            var abandoned = Path.Combine(cache.GenerationsDirectoryFor("local"), ".abandoned.tmp");
            var orphan = cache.GenerationDirectoryFor("local", "orphan");
            Directory.CreateDirectory(abandoned);
            Directory.CreateDirectory(orphan);

            await synchronizer.SyncAsync("local", null, CancellationToken.None);
            var after = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);

            Assert.IsNotNull(after);
            Assert.IsTrue(Directory.Exists(after.GenerationDirectory));
            Assert.IsFalse(Directory.Exists(before.GenerationDirectory));
            Assert.IsFalse(Directory.Exists(abandoned));
            Assert.IsFalse(Directory.Exists(orphan));
            Assert.AreSequenceEqual(
                [after.GenerationDirectory],
                Directory.EnumerateDirectories(cache.GenerationsDirectoryFor("local")));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task TryGetCurrentStateAsyncRejectsCommitMismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            Directory.CreateDirectory(Path.Combine(repository, "docs"));
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(Path.Combine(repository, "docs", "included.md"), "included");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "initial");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);
            await synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None);
            var state = cache.TryReadState("local");
            Assert.IsNotNull(state);
            cache.WriteState("local", state with { Commit = new string('0', 40) });

            var actual = await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None);

            Assert.IsNull(actual);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncAsyncSerializesConcurrentRequestsForOneSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            Directory.CreateDirectory(Path.Combine(repository, "docs"));
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(Path.Combine(repository, "docs", "included.md"), "included");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "initial");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);

            var results = await Task.WhenAll(
                synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None),
                synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None));

            Assert.AreEqual(pin, results[0].Commit);
            Assert.AreEqual(pin, results[1].Commit);
            Assert.IsTrue(cache.IsSynced("local"));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncAsyncUpdatesExistingCacheToHead()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            Directory.CreateDirectory(Path.Combine(repository, "docs"));
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            var includedPath = Path.Combine(repository, "docs", "included.md");
            await File.WriteAllTextAsync(includedPath, "pinned");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "pinned");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);
            await synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None);

            await File.WriteAllTextAsync(includedPath, "head");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "head");
            var head = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

            var result = await synchronizer.SyncAsync("local", "head", CancellationToken.None);

            Assert.AreEqual("head:main", result.Ref);
            Assert.AreEqual(head, result.Commit);
            Assert.AreEqual("head", await File.ReadAllTextAsync(Path.Combine(result.CacheDir, "docs", "included.md")));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncAsyncClonesPinnedCommitWithSparseCheckout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            Directory.CreateDirectory(Path.Combine(repository, "docs"));
            Directory.CreateDirectory(Path.Combine(repository, "other"));
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(Path.Combine(repository, "docs", "included.md"), "included");
            await File.WriteAllTextAsync(Path.Combine(repository, "other", "excluded.txt"), "excluded");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "initial");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);

            var result = await synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None);

            Assert.AreEqual("pinned", result.Ref);
            Assert.AreEqual(pin, result.Commit);
            Assert.IsTrue(File.Exists(Path.Combine(result.CacheDir, "docs", "included.md")));
            Assert.IsFalse(File.Exists(Path.Combine(result.CacheDir, "other", "excluded.txt")));
            Assert.IsTrue(cache.IsSynced("local"));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task TryGetCurrentStateAsyncRejectsDirtyOrIncompleteCheckout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var repository = await CreateRepositoryAsync(root, "origin", "included");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);
            await synchronizer.SyncAsync("local", null, CancellationToken.None);

            var snapshot = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(snapshot);
            await File.WriteAllTextAsync(Path.Combine(snapshot.RepositoryDirectory, "docs", "included.md"), "changed");
            Assert.IsNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));

            await RunGitAsync(snapshot.RepositoryDirectory, "checkout", "--", "docs/included.md");
            await File.WriteAllTextAsync(Path.Combine(snapshot.RepositoryDirectory, "docs", "untracked.md"), "fake");
            Assert.IsNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));
            File.Delete(Path.Combine(snapshot.RepositoryDirectory, "docs", "untracked.md"));
            Directory.Delete(Path.Combine(snapshot.RepositoryDirectory, "docs"), recursive: true);
            Assert.IsNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncAsyncReplacesCacheWhoseOriginDoesNotMatchConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var configured = await CreateRepositoryAsync(root, "configured", "configured");
            var substitute = await CreateRepositoryAsync(root, "substitute", "substitute");
            var pin = (await RunGitAsync(configured, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, configured, pin);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);
            await synchronizer.SyncAsync("local", null, CancellationToken.None);
            var rewriteKey = $"url.{substitute}.insteadOf";
            var snapshot = await synchronizer.TryGetCurrentSnapshotAsync("local", CancellationToken.None);
            Assert.IsNotNull(snapshot);
            await RunGitAsync(snapshot.RepositoryDirectory, "config", rewriteKey, configured);
            Assert.IsNotNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));
            await RunGitAsync(snapshot.RepositoryDirectory, "config", "--unset", rewriteKey);
            await RunGitAsync(snapshot.RepositoryDirectory, "remote", "set-url", "origin", substitute);

            var result = await synchronizer.SyncAsync("local", null, CancellationToken.None);

            Assert.AreEqual(pin, result.Commit);
            Assert.AreEqual("configured", await File.ReadAllTextAsync(Path.Combine(result.CacheDir, "docs", "included.md")));
            Assert.AreEqual(Path.GetFullPath(configured), Path.GetFullPath((await RunGitAsync(result.CacheDir, "remote", "get-url", "origin")).Trim()));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ValidationStatusRunsOnTheWalkTierNotTheQuickTier()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var synchronizer = new SourceSynchronizer(catalog, cache, WalkExpiresImmediately);

            var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                synchronizer.SyncAsync("local", null, CancellationToken.None));

            // Every tier here is generous except Walk. If `git status` were still Quick this sync
            // would succeed, and the ten-second ceiling that killed a real 13,485-file checkout
            // would be back.
            StringAssert.Contains(exception.Message, "git --no-optional-locks status");
            StringAssert.Contains(exception.Message, "Walk timeout");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SnapshotValidationStatusDoesNotTakeOptionalGitLocks()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            await new SourceSynchronizer(catalog, cache, GitTimeouts.Default)
                .SyncAsync("local", null, CancellationToken.None);

            var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                new SourceSynchronizer(catalog, cache, WalkExpiresImmediately)
                    .TryGetCurrentSnapshotAsync("local", CancellationToken.None));

            StringAssert.Contains(exception.Message, "git --no-optional-locks status");
            StringAssert.Contains(exception.Message, "Walk timeout");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FailedSyncKeepsItsStagingDirectoryInsteadOfDiscardingTheDownload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var synchronizer = new SourceSynchronizer(catalog, cache, WalkExpiresImmediately);

            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                synchronizer.SyncAsync("local", null, CancellationToken.None));

            var staging = Directory
                .EnumerateDirectories(cache.GenerationsDirectoryFor("local"), ".*.tmp")
                .ToList();
            Assert.AreEqual(
                1,
                staging.Count,
                "A failure discarded the download. For dotnet-api-docs that is 773 MB and about a "
                    + "minute of work thrown away every retry.");
            Assert.IsTrue(Directory.Exists(Path.Combine(staging[0], "repository", ".git")));
            Assert.IsFalse(
                File.Exists(Path.Combine(staging[0], "repository", ".git", "index.lock")),
                "Validation-only status left an index lock that makes the next Git command fail.");
            Assert.AreEqual(0, Directory.EnumerateDirectories(cache.Root, ".local-*.tmp").Count());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task DiscardedUnpublishedGenerationIsNeverResumed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                new SourceSynchronizer(catalog, cache, WalkExpiresImmediately)
                    .SyncAsync("local", null, CancellationToken.None));
            var abandoned = Directory
                .EnumerateDirectories(cache.GenerationsDirectoryFor("local"), ".*.tmp")
                .Single();
            var discarded = Path.Combine(
                cache.GenerationsDirectoryFor("local"),
                $".{Guid.NewGuid():N}.resume");
            await SourceSynchronizer.MoveDirectoryWhenReadyAsync(
                abandoned,
                discarded,
                TimeSpan.FromSeconds(10),
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(discarded, "repository", ".git", "resume-marker"),
                "must-not-survive");
            var progress = new RecordingProgress();

            var result = await new SourceSynchronizer(catalog, cache, GitTimeouts.Default)
                .SyncAsync("local", null, CancellationToken.None, progress);

            Assert.AreEqual("clone", progress.Values[0]);
            Assert.IsFalse(File.Exists(Path.Combine(result.CacheDir, ".git", "resume-marker")));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FailedResumeThatCannotBeDeletedRemainsDiscardedByItsDirectoryName()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Open files prevent directory deletion only on Windows.");

        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        FileStream? heldFile = null;
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                new SourceSynchronizer(catalog, cache, WalkExpiresImmediately)
                    .SyncAsync("local", null, CancellationToken.None));
            var progress = new CallbackProgress(value =>
            {
                if (!string.Equals(value, "resume", StringComparison.Ordinal))
                    return;

                var claimed = Directory
                    .EnumerateDirectories(cache.GenerationsDirectoryFor("local"))
                    .Single();
                heldFile = new FileStream(
                    Path.Combine(claimed, "repository", ".git", "config"),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
            });

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                new SourceSynchronizer(catalog, cache, GitTimeouts.Default)
                    .SyncAsync("local", null, CancellationToken.None, progress));
            heldFile!.Dispose();
            heldFile = null;

            var surviving = Directory
                .EnumerateDirectories(cache.GenerationsDirectoryFor("local"))
                .Single();
            StringAssert.EndsWith(surviving, ".resume");
        }
        finally
        {
            heldFile?.Dispose();
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task RetainedStagingIsResumedRatherThanClonedAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                new SourceSynchronizer(catalog, cache, WalkExpiresImmediately)
                    .SyncAsync("local", null, CancellationToken.None));
            var staging = Directory
                .EnumerateDirectories(cache.GenerationsDirectoryFor("local"), ".*.tmp")
                .Single();
            var stagedRepository = Path.Combine(staging, "repository");

            // Planted inside .git so it is invisible to `git status`, which the resumed attempt
            // must still find clean. Its survival into the cache directory is what proves the
            // download was reused rather than fetched again.
            await File.WriteAllTextAsync(
                Path.Combine(stagedRepository, ".git", "resume-marker"),
                "resumed");

            var origin = catalog.Sources.Single().Value.Url;
            await File.WriteAllTextAsync(Path.Combine(origin, "docs", "included.md"), "head");
            await RunGitAsync(origin, "add", ".");
            await RunGitAsync(origin, "commit", "-m", "head");
            var head = (await RunGitAsync(origin, "rev-parse", "HEAD")).Trim();
            var progress = new RecordingProgress();

            var result = await new SourceSynchronizer(catalog, cache, GitTimeouts.Default)
                .SyncAsync("local", "head", CancellationToken.None, progress);

            Assert.AreEqual(head, result.Commit);
            Assert.AreEqual(
                "head",
                await File.ReadAllTextAsync(Path.Combine(result.CacheDir, "docs", "included.md")));
            Assert.IsTrue(
                File.Exists(Path.Combine(result.CacheDir, ".git", "resume-marker")),
                "The retained staging directory was discarded and re-cloned instead of resumed.");
            Assert.AreSequenceEqual(
                ["resume", "sparse-checkout", "fetch", "checkout", "validate"],
                progress.Values,
                "A claimed download was published without rerunning the complete synchronization.");
            Assert.AreEqual(0, Directory.EnumerateDirectories(cache.Root, ".local-*.tmp").Count());
            Assert.AreEqual(
                0,
                Directory.EnumerateDirectories(cache.GenerationsDirectoryFor("local"), ".*.tmp").Count(),
                "A successful sync must leave no unpublished generation behind.");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncAppliesLargeCheckoutSettingsToTheCache()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var (catalog, cache) = await CreateFixtureAsync(root);
            var synchronizer = new SourceSynchronizer(catalog, cache, GitTimeouts.Default);

            var result = await synchronizer.SyncAsync("local", null, CancellationToken.None);

            // Set before the checkout that populates the index, so the checkout itself writes the
            // cheaper format rather than leaving it to a later rewrite.
            Assert.AreEqual(
                "true",
                (await RunGitAsync(result.CacheDir, "config", "--get", "feature.manyFiles")).Trim());
            Assert.AreEqual(
                "true",
                (await RunGitAsync(result.CacheDir, "config", "--get", "core.untrackedCache")).Trim());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    /// <summary>Walk expires before git can start; every other tier is generous.</summary>
    private static GitTimeouts WalkExpiresImmediately { get; } = new(
        Quick: TimeSpan.FromMinutes(2),
        Walk: TimeSpan.FromMilliseconds(1),
        Bulk: TimeSpan.FromMinutes(2));

    private static async Task<(SourceCatalog Catalog, SourceCache Cache)> CreateFixtureAsync(string root)
    {
        var repository = await CreateRepositoryAsync(root, "origin", "included");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        return (new SourceCatalog(catalogPath), new SourceCache(Path.Combine(root, "cache")));
    }

    private static async Task<PackageSynchronizerFixture> CreatePackageFixtureAsync(
        string root,
        IReadOnlyList<string> frameworks)
    {
        var repository = Path.Combine(root, "origin");
        Directory.CreateDirectory(repository);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        PackageSyncFixture.WriteManifest(repository, "5.3.0");
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "manifest");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WritePackageCatalogAsync(catalogPath, repository, pin);
        var packagePath = Path.Combine(root, "initial.nupkg");
        PackageSyncFixture.CreatePackage(packagePath, frameworks);
        var sha512 = Convert.ToBase64String(Enumerable.Repeat((byte)7, 64).ToArray());
        var client = new FixtureNuGetPackageClient(packagePath, sha512);
        return new PackageSynchronizerFixture(
            new SourceCatalog(catalogPath),
            new SourceCache(Path.Combine(root, "cache")),
            client,
            sha512);
    }

    private static async Task WritePackageCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["roslyn-api-docs"] = new
                {
                    repository = "dotnet/roslyn-api-docs",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "dotnet/xml" },
                    purpose = "Fixture Roslyn API source.",
                    apiPackages = new[] { PackageSyncFixture.Package("5.3.0") },
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task WriteNonRoslynPackageCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["other"] = new
                {
                    repository = "example/other-api-docs",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Fixture non-Roslyn API source.",
                    apiPackages = new[] { PackageSyncFixture.Package("5.3.0") },
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> CreateRepositoryAsync(string root, string name, string contents)
    {
        var repository = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(repository, "docs"));
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(repository, "docs", "included.md"), contents);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "initial");
        return repository;
    }

    private static async Task WriteCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["local"] = new
                {
                    repository = "test/local",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test source.",
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> RunGitAsync(string? workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }

    private sealed class RecordingContributor : ISourceGenerationContributor
    {
        public Exception? Failure { get; set; }

        private bool Pause { get; set; }

        public TaskCompletionSource Started { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Continue { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void PauseOnNextBuild()
        {
            Pause = true;
            Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Continue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public bool AppliesTo(SourceDefinition definition) => true;

        public async Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
            SourceDefinition definition,
            string refLabel,
            string repositoryDirectory,
            string supplementsDirectory,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(supplementsDirectory);
            Started.TrySetResult();
            if (Pause)
                await Continue.Task.WaitAsync(cancellationToken);
            if (Failure is not null)
                throw Failure;

            await File.WriteAllTextAsync(
                Path.Combine(supplementsDirectory, "contributor.complete"),
                refLabel,
                cancellationToken);
            return [];
        }
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Values { get; } = [];

        public void Report(string value) => Values.Add(value);
    }

    private sealed class CallbackProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }

    private sealed class CancelingContributor(CancellationTokenSource cancellation) : ISourceGenerationContributor
    {
        public bool AppliesTo(SourceDefinition definition) => true;

        public Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
            SourceDefinition definition,
            string refLabel,
            string repositoryDirectory,
            string supplementsDirectory,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<ApiPackageSyncState>>([]);
        }
    }

    private sealed record PackageSynchronizerFixture(
        SourceCatalog Catalog,
        SourceCache Cache,
        FixtureNuGetPackageClient Client,
        string ClientSha512);
}
