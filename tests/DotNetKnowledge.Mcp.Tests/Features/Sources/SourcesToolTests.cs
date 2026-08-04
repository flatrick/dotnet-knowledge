using System.Text.Json;
using System.Diagnostics;
using DotNetKnowledge.Mcp.Features.Sources;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.Sources;

[TestClass]
public sealed class SourcesToolTests
{
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
                CancellationToken.None,
                @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("sync_failed", document.RootElement.GetProperty("error").GetString());
            Assert.IsFalse(cache.IsSynced("local"));
            Assert.IsFalse(Directory.Exists(cache.DirectoryFor("local")));
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
            var synchronizer = new SourceSynchronizer(new SourceCatalog(catalogPath), cache);

            var json = await SourcesTool.SyncSource(
                "local",
                synchronizer,
                CancellationToken.None,
                @ref: null);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("local", document.RootElement.GetProperty("name").GetString());
            Assert.AreEqual(cache.DirectoryFor("local"), document.RootElement.GetProperty("cacheDir").GetString());
            var source = document.RootElement.GetProperty("source");
            Assert.AreEqual("test/local", source.GetProperty("repo").GetString());
            Assert.AreEqual("pinned", source.GetProperty("ref").GetString());
            Assert.AreEqual(pin, source.GetProperty("commit").GetString());
            Assert.IsTrue(source.TryGetProperty("fetchedAt", out _));
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
            Assert.AreEqual(5, sources.GetArrayLength());
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
