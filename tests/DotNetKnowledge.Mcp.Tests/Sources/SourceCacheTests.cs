using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
[DoNotParallelize]
public sealed class SourceCacheTests
{
    [TestMethod]
    public void CompletionStateRejectsGenerationNamesThatEscapeTheGenerationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var cache = new SourceCache(root);
            cache.WriteState("csharplang", CreateState("C:escape"));

            Assert.IsNull(cache.TryReadState("csharplang"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void IsSyncedReturnsFalseWhenTheGenerationHasNoSupplementsDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var cache = new SourceCache(root);
            var state = CreateState("generation-1");
            Directory.CreateDirectory(Path.Combine(
                cache.RepositoryDirectoryFor("csharplang", state.Generation),
                ".git"));
            cache.WriteState("csharplang", state);

            Assert.IsFalse(cache.IsSynced("csharplang"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void GenerationPathsStayUnderTheSourceGenerationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var cache = new SourceCache(root);

        Assert.AreEqual(
            Path.Combine(root, ".generations", "csharplang"),
            cache.GenerationsDirectoryFor("csharplang"));
        Assert.AreEqual(
            Path.Combine(root, ".generations", "csharplang", "generation-1"),
            cache.GenerationDirectoryFor("csharplang", "generation-1"));
        Assert.AreEqual(
            Path.Combine(root, ".generations", "csharplang", "generation-1", "repository"),
            cache.RepositoryDirectoryFor("csharplang", "generation-1"));
        Assert.AreEqual(
            Path.Combine(root, ".generations", "csharplang", "generation-1", "supplements"),
            cache.SupplementsDirectoryFor("csharplang", "generation-1"));
    }

    [TestMethod]
    public void IsSyncedReturnsFalseForIncompleteCompletionState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var cache = new SourceCache(root);
            var directory = cache.DirectoryFor("csharplang");
            Directory.CreateDirectory(Path.Combine(directory, ".git"));
            Directory.CreateDirectory(Path.GetDirectoryName(cache.StatePathFor("csharplang"))!);
            File.WriteAllText(cache.StatePathFor("csharplang"), "{}");

            Assert.IsFalse(cache.IsSynced("csharplang"));
            Assert.IsNull(cache.TryReadState("csharplang"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void IsSyncedReturnsFalseForMalformedCompletionState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var cache = new SourceCache(root);
            var directory = cache.DirectoryFor("csharplang");
            Directory.CreateDirectory(Path.Combine(directory, ".git"));
            Directory.CreateDirectory(Path.GetDirectoryName(cache.StatePathFor("csharplang"))!);
            File.WriteAllText(cache.StatePathFor("csharplang"), "not json");

            Assert.IsFalse(cache.IsSynced("csharplang"));
            Assert.IsNull(cache.TryReadState("csharplang"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CompletionStateRoundTripsProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var cache = new SourceCache(root);
            var state = new SourceSyncState(
                SchemaVersion: 2,
                Name: "csharplang",
                Repository: "dotnet/csharplang",
                Url: "https://github.com/dotnet/csharplang.git",
                Ref: "pinned",
                Commit: "0123456789012345678901234567890123456789",
                FetchedAt: new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
                SparsePaths: ["proposals", "spec"],
                Generation: "generation-1",
                ApiPackages:
                [
                    new ApiPackageSyncState(
                        PackageId: "Microsoft.CodeAnalysis.Common",
                        AssemblyName: "Microsoft.CodeAnalysis",
                        Version: "5.0.0",
                        Sha512: Convert.ToBase64String(new byte[64]),
                        Feed: "https://api.nuget.org/v3/index.json",
                        FetchedAt: new DateTimeOffset(2026, 8, 4, 12, 31, 0, TimeSpan.Zero),
                        DefaultFramework: "net9.0",
                        AvailableFrameworks: ["net8.0", "net9.0"],
                        CorpusDirectory: "packages/microsoft.codeanalysis.common/5.0.0/net9.0")
                ]);

            cache.WriteState("csharplang", state);

            var actual = cache.TryReadState("csharplang");
            Assert.IsNotNull(actual);
            Assert.AreEqual(state.Name, actual.Name);
            Assert.AreEqual(state.Repository, actual.Repository);
            Assert.AreEqual(state.Url, actual.Url);
            Assert.AreEqual(state.Ref, actual.Ref);
            Assert.AreEqual(state.Commit, actual.Commit);
            Assert.AreEqual(state.FetchedAt, actual.FetchedAt);
            Assert.AreEqual(state.Generation, actual.Generation);
            CollectionAssert.AreEqual(state.SparsePaths.ToArray(), actual.SparsePaths.ToArray());
            Assert.HasCount(1, actual.ApiPackages);
            var expectedPackage = state.ApiPackages[0];
            var actualPackage = actual.ApiPackages[0];
            Assert.AreEqual(expectedPackage.PackageId, actualPackage.PackageId);
            Assert.AreEqual(expectedPackage.AssemblyName, actualPackage.AssemblyName);
            Assert.AreEqual(expectedPackage.Version, actualPackage.Version);
            Assert.AreEqual(expectedPackage.Sha512, actualPackage.Sha512);
            Assert.AreEqual(expectedPackage.Feed, actualPackage.Feed);
            Assert.AreEqual(expectedPackage.FetchedAt, actualPackage.FetchedAt);
            Assert.AreEqual(expectedPackage.DefaultFramework, actualPackage.DefaultFramework);
            Assert.AreSequenceEqual(expectedPackage.AvailableFrameworks, actualPackage.AvailableFrameworks);
            Assert.AreEqual(expectedPackage.CorpusDirectory, actualPackage.CorpusDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void IsSyncedReturnsFalseForRepositoryWithoutCompletionState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable(SourceCache.CacheEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(SourceCache.CacheEnvironmentVariable, root);
            Directory.CreateDirectory(Path.Combine(root, "csharplang", ".git"));

            var cache = new SourceCache();

            Assert.IsFalse(cache.IsSynced("csharplang"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SourceCache.CacheEnvironmentVariable, previous);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SourceSyncState CreateState(string generation) => new(
        SchemaVersion: 2,
        Name: "csharplang",
        Repository: "dotnet/csharplang",
        Url: "https://github.com/dotnet/csharplang.git",
        Ref: "pinned",
        Commit: "0123456789012345678901234567890123456789",
        FetchedAt: new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
        SparsePaths: ["proposals", "spec"],
        Generation: generation,
        ApiPackages: []);
}
