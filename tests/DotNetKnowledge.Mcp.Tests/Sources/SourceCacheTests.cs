using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
[DoNotParallelize]
public sealed class SourceCacheTests
{
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
                SchemaVersion: 1,
                Name: "csharplang",
                Repository: "dotnet/csharplang",
                Url: "https://github.com/dotnet/csharplang.git",
                Ref: "pinned",
                Commit: "0123456789012345678901234567890123456789",
                FetchedAt: new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
                SparsePaths: ["proposals", "spec"]);

            cache.WriteState("csharplang", state);

            var actual = cache.TryReadState("csharplang");
            Assert.IsNotNull(actual);
            Assert.AreEqual(state.Name, actual.Name);
            Assert.AreEqual(state.Repository, actual.Repository);
            Assert.AreEqual(state.Url, actual.Url);
            Assert.AreEqual(state.Ref, actual.Ref);
            Assert.AreEqual(state.Commit, actual.Commit);
            Assert.AreEqual(state.FetchedAt, actual.FetchedAt);
            CollectionAssert.AreEqual(state.SparsePaths.ToArray(), actual.SparsePaths.ToArray());
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
}
