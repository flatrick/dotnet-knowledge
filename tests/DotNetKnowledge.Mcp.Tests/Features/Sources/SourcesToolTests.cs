using System.Text.Json;
using System.Diagnostics;
using DotNetKnowledge.Mcp.Features.Sources;
using DotNetKnowledge.Mcp.Sources;
using ModelContextProtocol;

namespace DotNetKnowledge.Mcp.Tests.Features.Sources;

[TestClass]
public sealed class SourcesToolTests
{
    private static readonly string[] ExpectedStages =
        ["clone", "sparse-checkout", "fetch", "checkout", "validate"];

    private static readonly string[] ExpectedConfiguredSupplementFrameworks = ["net10.0"];

    // These tests call the static tool method directly rather than through the MCP SDK's
    // parameter injection, so they must supply a progress reporter themselves; nothing here
    // reads it.
    private static readonly IProgress<ProgressNotificationValue> NoOpProgress =
        new Progress<ProgressNotificationValue>();

    [TestMethod]
    public async Task SyncSourceDoesNotPublishFailedGitOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalogPath = Path.Combine(root, "sources.json");
            Directory.CreateDirectory(root);
            await WriteCatalogAsync(
                catalogPath,
                Path.Combine(root, "missing-origin"),
                new string('0', 40));
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);

            var json = await SourcesTool.SyncSource(
                "local",
                synchronizer,
                NoOpProgress,
                CancellationToken.None,
                @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("sync_failed", document.RootElement.GetProperty("error").GetString());
            Assert.IsFalse(cache.IsSynced("local"));
            var generationsDirectory = cache.GenerationsDirectoryFor("local");
            Assert.AreEqual(
                1,
                Directory.EnumerateDirectories(generationsDirectory, ".*.tmp").Count(),
                "A failed operation must remain unpublished but retain its in-progress generation.");
            Assert.AreEqual(
                0,
                Directory.EnumerateDirectories(generationsDirectory)
                    .Count(path => !Path.GetFileName(path).StartsWith('.')),
                "A failed operation published a completed generation.");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourcePropagatesCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalog = new SourceCatalog();
            var synchronizer = new SourceSynchronizer(catalog, new SourceCache(root));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => SourcesTool.SyncSource(
                "csharplang",
                synchronizer,
                NoOpProgress,
                cancellation.Token,
                @ref: null));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SyncSourceReturnsStructuredInvalidRefError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalog = new SourceCatalog();
            var synchronizer = new SourceSynchronizer(catalog, new SourceCache(root));

            var json = await SourcesTool.SyncSource(
                "csharplang",
                synchronizer,
                NoOpProgress,
                CancellationToken.None,
                @ref: "main");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_ref", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "omitted or \"head\"");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SyncSourceReturnsCachePathAndProvenance()
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
            var catalog = new SourceCatalog(catalogPath);
            var synchronizer = new SourceSynchronizer(catalog, cache);

            var json = await SourcesTool.SyncSource(
                "local",
                synchronizer,
                NoOpProgress,
                CancellationToken.None,
                @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("local", document.RootElement.GetProperty("name").GetString());
            var state = cache.TryReadState("local");
            Assert.IsNotNull(state);
            var repositoryDirectory = cache.RepositoryDirectoryFor("local", state.Generation);
            Assert.AreEqual(repositoryDirectory, document.RootElement.GetProperty("cacheDir").GetString());
            var source = document.RootElement.GetProperty("source");
            Assert.AreEqual("test/local", source.GetProperty("repo").GetString());
            Assert.AreEqual("pinned", source.GetProperty("ref").GetString());
            Assert.AreEqual(pin, source.GetProperty("commit").GetString());
            Assert.IsTrue(source.TryGetProperty("fetchedAt", out _));

            var statusJson = await SourcesTool.ListSources(
                catalog,
                cache,
                synchronizer,
                CancellationToken.None);
            using var statusDocument = JsonDocument.Parse(statusJson);
            var status = statusDocument.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.AreEqual(repositoryDirectory, status.GetProperty("cacheDir").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SourceStatusReportsConfiguredSupplementAndVerifiedSynchronizationState()
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
            await WritePackageCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache, [new StateReportingContributor()]);

            var before = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using (var beforeDocument = JsonDocument.Parse(before))
            {
                var source = beforeDocument.RootElement.GetProperty("sources").EnumerateArray().Single();
                Assert.IsFalse(source.GetProperty("synced").GetBoolean());
                var supplement = source.GetProperty("supplements").EnumerateArray().Single();
                Assert.AreEqual("Fixture.Package", supplement.GetProperty("packageId").GetString());
                Assert.AreEqual("Fixture.Package", supplement.GetProperty("assemblyName").GetString());
                Assert.AreEqual("https://api.nuget.org/v3/index.json", supplement.GetProperty("feed").GetString());
                Assert.AreEqual("5.3.0", supplement.GetProperty("version").GetString());
                Assert.IsFalse(supplement.GetProperty("synced").GetBoolean());
                Assert.AreEqual("net10.0", supplement.GetProperty("defaultFramework").GetString());
                Assert.AreEqual(0, supplement.GetProperty("availableFrameworks").GetArrayLength());
            }

            var sync = await SourcesTool.SyncSource(
                "local", synchronizer, NoOpProgress, CancellationToken.None, @ref: null);
            using (var syncDocument = JsonDocument.Parse(sync))
            {
                var supplement = syncDocument.RootElement.GetProperty("supplements").EnumerateArray().Single();
                Assert.IsTrue(supplement.GetProperty("synced").GetBoolean());
                Assert.AreEqual("5.3.0", supplement.GetProperty("version").GetString());
                Assert.AreEqual(Convert.ToBase64String(new byte[64]), supplement.GetProperty("sha512").GetString());
                Assert.AreEqual("net10.0", supplement.GetProperty("defaultFramework").GetString());
                CollectionAssert.AreEqual(
                    ExpectedConfiguredSupplementFrameworks,
                    supplement.GetProperty("availableFrameworks").EnumerateArray().Select(value => value.GetString()).ToArray());
                Assert.IsTrue(supplement.TryGetProperty("fetchedAt", out _));
            }

            var after = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using var afterDocument = JsonDocument.Parse(after);
            var status = afterDocument.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.IsTrue(status.GetProperty("synced").GetBoolean());
            Assert.IsTrue(status.GetProperty("supplements").EnumerateArray().Single().GetProperty("synced").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("hash")]
    [DataRow("assembly")]
    [DataRow("feed")]
    [DataRow("default")]
    [DataRow("available-frameworks")]
    [DataRow("corpus-path")]
    [DataRow("duplicate")]
    public async Task SourceStatusRejectsMismatchedOrAmbiguousPinnedSupplementState(string mutation)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache, synchronizer) = await CreateSynchronizedPackageSourceAsync(root);
            var current = cache.TryReadState("local")!;
            var package = current.ApiPackages.Single();
            var mutated = mutation switch
            {
                "version" => package with { Version = "5.3.1" },
                "hash" => package with { Sha512 = Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray()) },
                "assembly" => package with { AssemblyName = "Other.Assembly" },
                "feed" => package with { Feed = "https://example.invalid/v3/index.json" },
                "default" => package with { DefaultFramework = "net8.0" },
                "available-frameworks" => package with { AvailableFrameworks = ["net8.0"] },
                "corpus-path" => package with { CorpusDirectory = "../outside" },
                "duplicate" => package,
                _ => throw new AssertFailedException($"Unknown mutation '{mutation}'."),
            };
            cache.WriteState(
                "local",
                current with
                {
                    ApiPackages = mutation == "duplicate"
                        ? [package, package with { PackageId = "FIXTURE.PACKAGE" }]
                        : [mutated],
                });

            var json = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.IsFalse(source.GetProperty("synced").GetBoolean(), mutation);
            var supplement = source.GetProperty("supplements").EnumerateArray().Single();
            Assert.IsFalse(supplement.GetProperty("synced").GetBoolean(), mutation);
            Assert.AreEqual("Fixture.Package", supplement.GetProperty("packageId").GetString(), mutation);
            Assert.AreEqual("Fixture.Package", supplement.GetProperty("assemblyName").GetString(), mutation);
            Assert.AreEqual("https://api.nuget.org/v3/index.json", supplement.GetProperty("feed").GetString(), mutation);
            Assert.AreEqual("5.3.0", supplement.GetProperty("version").GetString(), mutation);
            Assert.AreEqual(Convert.ToBase64String(new byte[64]), supplement.GetProperty("sha512").GetString(), mutation);
            Assert.AreEqual("net10.0", supplement.GetProperty("defaultFramework").GetString(), mutation);
            Assert.AreEqual(0, supplement.GetProperty("availableFrameworks").GetArrayLength(), mutation);
            Assert.IsFalse(supplement.TryGetProperty("fetchedAt", out _), mutation);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SourceStatusIgnoresUnrelatedNullPackageStateAndTreatsNullOnlyStateAsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache, synchronizer) = await CreateSynchronizedPackageSourceAsync(root);
            var current = cache.TryReadState("local")!;
            var package = current.ApiPackages.Single();
            cache.WriteState("local", current with { ApiPackages = [null!, package] });

            var withUnrelatedNull = await SourcesTool.ListSources(
                catalog, cache, synchronizer, CancellationToken.None);
            using (var document = JsonDocument.Parse(withUnrelatedNull))
            {
                var source = document.RootElement.GetProperty("sources").EnumerateArray().Single();
                Assert.IsTrue(source.GetProperty("synced").GetBoolean());
                Assert.IsTrue(source.GetProperty("supplements").EnumerateArray().Single().GetProperty("synced").GetBoolean());
            }

            cache.WriteState("local", current with { ApiPackages = [null!] });
            var withOnlyNull = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using var nullOnlyDocument = JsonDocument.Parse(withOnlyNull);
            var nullOnlySource = nullOnlyDocument.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.IsFalse(nullOnlySource.GetProperty("synced").GetBoolean());
            Assert.IsFalse(nullOnlySource.GetProperty("supplements").EnumerateArray().Single().GetProperty("synced").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    [DataRow("net8.0", true)]
    [DataRow("net<10.0", false)]
    [DataRow("net\u0001", false)]
    [DataRow("net10.0.", false)]
    [DataRow("net10.0 ", false)]
    [DataRow("NUL.framework", false)]
    public async Task SourceStatusRequiresPortableAvailableFrameworkFilenameStems(
        string extraFramework,
        bool expectedSynced)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache, synchronizer) = await CreateSynchronizedPackageSourceAsync(root);
            var current = cache.TryReadState("local")!;
            var package = current.ApiPackages.Single();
            cache.WriteState(
                "local",
                current with { ApiPackages = [package with { AvailableFrameworks = ["net10.0", extraFramework] }] });

            var json = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.AreEqual(expectedSynced, source.GetProperty("synced").GetBoolean(), extraFramework);
            var supplement = source.GetProperty("supplements").EnumerateArray().Single();
            Assert.AreEqual(expectedSynced, supplement.GetProperty("synced").GetBoolean(), extraFramework);
            Assert.AreEqual(
                expectedSynced ? 2 : 0,
                supplement.GetProperty("availableFrameworks").GetArrayLength(),
                extraFramework);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SourceStatusRejectsReservedDefaultFrameworkFilenameStem()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, cache, synchronizer) = await CreatePackageSourceAsync(
                root,
                new StateReportingContributor(defaultFramework: "COM1"),
                configuredDefaultFramework: "COM1");
            await synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None);

            var json = await SourcesTool.ListSources(catalog, cache, synchronizer, CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.IsFalse(source.GetProperty("synced").GetBoolean());
            Assert.IsFalse(source.GetProperty("supplements").EnumerateArray().Single().GetProperty("synced").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourceReportsPinnedMismatchedSupplementUsingConfiguredIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (catalog, _, synchronizer) = await CreatePackageSourceAsync(
                root,
                new StateReportingContributor(version: "5.3.1", sha512: Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray())));

            var json = await SourcesTool.SyncSource(
                "local", synchronizer, NoOpProgress, CancellationToken.None, @ref: null);

            using var document = JsonDocument.Parse(json);
            var supplement = document.RootElement.GetProperty("supplements").EnumerateArray().Single();
            Assert.IsFalse(supplement.GetProperty("synced").GetBoolean());
            Assert.AreEqual("5.3.0", supplement.GetProperty("version").GetString());
            Assert.AreEqual(Convert.ToBase64String(new byte[64]), supplement.GetProperty("sha512").GetString());
            Assert.AreEqual(0, supplement.GetProperty("availableFrameworks").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourceReportsObservedSupplementIdentityForHead()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var observedHash = Convert.ToBase64String(Enumerable.Repeat((byte)1, 64).ToArray());
            var (_, _, synchronizer) = await CreatePackageSourceAsync(
                root,
                new StateReportingContributor(version: "5.3.1", sha512: observedHash));

            var json = await SourcesTool.SyncSource(
                "local", synchronizer, NoOpProgress, CancellationToken.None, @ref: "head");

            using var document = JsonDocument.Parse(json);
            var supplement = document.RootElement.GetProperty("supplements").EnumerateArray().Single();
            Assert.IsTrue(supplement.GetProperty("synced").GetBoolean());
            Assert.AreEqual("5.3.1", supplement.GetProperty("version").GetString());
            Assert.AreEqual(observedHash, supplement.GetProperty("sha512").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    // The feed is the one dependency the server cannot validate in advance, so a 404 or a refused
    // connection has to reach the caller as a stated cause. An opaque failure is indistinguishable
    // from a crash, and an agent cannot tell a retryable outage from a wrong pin.
    [TestMethod]
    public async Task SyncSourceReportsAFeedFailureWithItsCause()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (_, _, synchronizer) = await CreatePackageSourceAsync(
                root,
                new ThrowingContributor(new HttpRequestException(
                    "Response status code does not indicate success: 404 (Not Found).")));

            var json = await SourcesTool.SyncSource(
                "local", synchronizer, NoOpProgress, CancellationToken.None, @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("sync_failed", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "404");
            Assert.AreEqual("local", document.RootElement.GetProperty("source").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    // InvalidDataException is what the whole package pipeline raises for a failed check, and the
    // hash mismatch is the one that matters most: a caller told only "an error occurred" cannot
    // tell a tampered or mis-pinned package from a disk problem.
    [TestMethod]
    public async Task SyncSourceReportsAPackageHashMismatchWithItsCause()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var (_, _, synchronizer) = await CreatePackageSourceAsync(
                root,
                new ThrowingContributor(new InvalidDataException(
                    "The NuGet package hash does not match the catalog hash.")));

            var json = await SourcesTool.SyncSource(
                "local", synchronizer, NoOpProgress, CancellationToken.None, @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("sync_failed", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(
                document.RootElement.GetProperty("message").GetString(),
                "does not match the catalog hash");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourcePublishesProgressNotificationsInStageOrder()
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

            // SourcesTool.SyncSource used to wrap its stage reporter in System.Progress<T>, whose
            // callbacks post to the thread pool when there is no synchronization context, as here.
            // A recording IProgress<T> that runs inline is the only way to pin down stage order and
            // the running count without a race.
            var recorder = new RecordingProgressNotifications();
            await SourcesTool.SyncSource(
                "local",
                synchronizer,
                recorder,
                CancellationToken.None,
                @ref: null);

            // One notification per stage, each stage named exactly once, and a running count that
            // reaches the denominator exactly. A skipped or doubled stage leaves the count short of
            // its total or past it, which is the failure the client would see as a stalled bar.
            Assert.HasCount(ExpectedStages.Length, recorder.Notifications);
            for (var index = 0; index < ExpectedStages.Length; index++)
            {
                var notification = recorder.Notifications[index];
                Assert.AreEqual((float)(index + 1), notification.Progress);
                Assert.AreEqual((float)ExpectedStages.Length, notification.Total);
                Assert.AreEqual($"local: {ExpectedStages[index]}", notification.Message);
            }

            Assert.AreEqual(
                recorder.Notifications[^1].Total,
                recorder.Notifications[^1].Progress);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourceUsesCompositeProgressDenominatorForPackageSupplements()
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
            await WritePackageCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var synchronizer = new SourceSynchronizer(
                catalog,
                new SourceCache(Path.Combine(root, "cache")),
                [new StageOnlyContributor()]);
            var recorder = new RecordingProgressNotifications();

            await SourcesTool.SyncSource(
                "local",
                synchronizer,
                recorder,
                CancellationToken.None,
                @ref: null);

            var expected = ExpectedStages.Concat(
                ["package-download", "package-validate", "package-normalize"]).ToArray();
            Assert.HasCount(expected.Length, recorder.Notifications);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual((float)(index + 1), recorder.Notifications[index].Progress);
                Assert.AreEqual((float)expected.Length, recorder.Notifications[index].Total);
                Assert.AreEqual($"local: {expected[index]}", recorder.Notifications[index].Message);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task ListSourcesReportsValidatedSynchronizationState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalog = new SourceCatalog();
            var cache = new SourceCache(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);

            var json = await SourcesTool.ListSources(
                catalog,
                cache,
                synchronizer,
                CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(root, document.RootElement.GetProperty("cacheRoot").GetString());
            var sources = document.RootElement.GetProperty("sources");
            Assert.AreEqual(6, sources.GetArrayLength());
            foreach (var source in sources.EnumerateArray())
                Assert.IsFalse(source.GetProperty("synced").GetBoolean());
            Assert.AreEqual(
                "dotnet/csharplang",
                sources.EnumerateArray()
                    .Single(source => source.GetProperty("name").GetString() == "csharplang")
                    .GetProperty("repository")
                    .GetString());
            StringAssert.Contains(document.RootElement.GetProperty("nextStep").GetString(), "Call sync_source");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SyncAsyncReportsEveryStageInOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            var contentDirectory = Path.Combine(repository, "docs");
            Directory.CreateDirectory(contentDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(
                Path.Combine(contentDirectory, "Widget.xml"),
                "<Type Name=\"Widget\" FullName=\"System.Widget\" />");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "docs");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var synchronizer = new SourceSynchronizer(
                new SourceCatalog(catalogPath),
                new SourceCache(Path.Combine(root, "cache")));

            // A plain IProgress<T> that records inline. System.Progress<T> posts its callbacks
            // through the synchronization context, so a test using it would have to sleep before
            // asserting and would still be racy.
            var recorder = new RecordingProgress();
            await synchronizer.SyncAsync(
                "local",
                requestedRef: null,
                CancellationToken.None,
                recorder);

            CollectionAssert.AreEqual(
                ExpectedStages,
                recorder.Stages.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SyncSourceReturnsStructuredUnknownSourceError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalog = new SourceCatalog();
            var synchronizer = new SourceSynchronizer(catalog, new SourceCache(root));

            var json = await SourcesTool.SyncSource(
                "missing",
                synchronizer,
                NoOpProgress,
                CancellationToken.None,
                @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("unknown_source", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(
                document.RootElement.GetProperty("message").GetString(),
                "Call list_sources");
            Assert.AreEqual("missing", document.RootElement.GetProperty("source").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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

    private static async Task WritePackageCatalogAsync(
        string path,
        string repository,
        string pin,
        string defaultFramework = "net10.0")
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["local"] = new
                {
                    repository = "dotnet/roslyn-api-docs",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test source.",
                    apiPackages = new[]
                    {
                        new
                        {
                            packageId = "Fixture.Package",
                            assemblyName = "Fixture.Package",
                            feed = "https://api.nuget.org/v3/index.json",
                            version = "5.3.0",
                            sha512 = Convert.ToBase64String(new byte[64]),
                            defaultFramework,
                        },
                    },
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<(SourceCatalog Catalog, SourceCache Cache, SourceSynchronizer Synchronizer)>
        CreateSynchronizedPackageSourceAsync(string root)
    {
        var result = await CreatePackageSourceAsync(root, new StateReportingContributor());
        await result.Synchronizer.SyncAsync("local", requestedRef: null, CancellationToken.None);
        return result;
    }

    private static async Task<(SourceCatalog Catalog, SourceCache Cache, SourceSynchronizer Synchronizer)>
        CreatePackageSourceAsync(
            string root,
            ISourceGenerationContributor contributor,
            string configuredDefaultFramework = "net10.0")
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
        await WritePackageCatalogAsync(catalogPath, repository, pin, configuredDefaultFramework);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        return (catalog, cache, new SourceSynchronizer(catalog, cache, [contributor]));
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

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Stages { get; } = [];

        public void Report(string value) => Stages.Add(value);
    }

    private sealed class RecordingProgressNotifications : IProgress<ProgressNotificationValue>
    {
        public List<ProgressNotificationValue> Notifications { get; } = [];

        public void Report(ProgressNotificationValue value) => Notifications.Add(value);
    }

    private sealed class StageOnlyContributor : ISourceGenerationContributor
    {
        public bool AppliesTo(SourceDefinition definition) => definition.ApiPackages is { Count: > 0 };

        public Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
            SourceDefinition definition,
            string refLabel,
            string repositoryDirectory,
            string supplementsDirectory,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report("package-download");
            progress?.Report("package-validate");
            progress?.Report("package-normalize");
            return Task.FromResult<IReadOnlyList<ApiPackageSyncState>>([]);
        }
    }

    private sealed class ThrowingContributor(Exception exception) : ISourceGenerationContributor
    {
        public bool AppliesTo(SourceDefinition definition) => definition.ApiPackages is { Count: > 0 };

        public Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
            SourceDefinition definition,
            string refLabel,
            string repositoryDirectory,
            string supplementsDirectory,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report("package-download");
            throw exception;
        }
    }

    private sealed class StateReportingContributor(
        string version = "5.3.0",
        string? sha512 = null,
        string defaultFramework = "net10.0") : ISourceGenerationContributor
    {
        public bool AppliesTo(SourceDefinition definition) => definition.ApiPackages is { Count: > 0 };

        public Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
            SourceDefinition definition,
            string refLabel,
            string repositoryDirectory,
            string supplementsDirectory,
            IProgress<string>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApiPackageSyncState>>(
            [
                new ApiPackageSyncState(
                    PackageId: "fixture.package",
                    AssemblyName: "Fixture.Package",
                    Version: version,
                    Sha512: sha512 ?? Convert.ToBase64String(new byte[64]),
                    Feed: "https://api.nuget.org/v3/index.json",
                    FetchedAt: DateTimeOffset.UnixEpoch,
                    DefaultFramework: defaultFramework,
                    AvailableFrameworks: [defaultFramework],
                    CorpusDirectory: "corpus/fixture.package"),
            ]);
    }
}
