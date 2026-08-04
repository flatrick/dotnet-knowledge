using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class SourceSynchronizerTests
{
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

            await File.WriteAllTextAsync(Path.Combine(cache.DirectoryFor("local"), "docs", "included.md"), "changed");
            Assert.IsNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));

            await RunGitAsync(cache.DirectoryFor("local"), "checkout", "--", "docs/included.md");
            await File.WriteAllTextAsync(Path.Combine(cache.DirectoryFor("local"), "docs", "untracked.md"), "fake");
            Assert.IsNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));
            File.Delete(Path.Combine(cache.DirectoryFor("local"), "docs", "untracked.md"));
            Directory.Delete(Path.Combine(cache.DirectoryFor("local"), "docs"), recursive: true);
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
            await RunGitAsync(cache.DirectoryFor("local"), "config", rewriteKey, configured);
            Assert.IsNotNull(await synchronizer.TryGetCurrentStateAsync("local", CancellationToken.None));
            await RunGitAsync(cache.DirectoryFor("local"), "config", "--unset", rewriteKey);
            await RunGitAsync(cache.DirectoryFor("local"), "remote", "set-url", "origin", substitute);

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
}
