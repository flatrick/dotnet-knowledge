using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.LanguageDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.LanguageDocs;

[TestClass]
public sealed class LanguageDocsQueryServiceTests
{
    private const string ProposalA =
        "# Feature A\n" +
        "\n" +
        "## Motivation\n" +
        "\n" +
        "Some motivating prose about feature A.\n" +
        "\n" +
        "## Detailed design\n" +
        "\n" +
        "```csharp\n" +
        "class Foo { }\n" +
        "```\n" +
        "\n" +
        "## Alternatives\n" +
        "\n" +
        "### Alternative 1\n" +
        "\n" +
        "Alternative text.\n";

    private const string ProposalB =
        "# Feature B\n" +
        "\n" +
        "## Summary\n" +
        "\n" +
        "Summary text mentioning FeatureA for cross-file search.\n";

    [TestMethod]
    public async Task GetOutlineAsyncReturnsHeadingsAndPaginates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var first = await service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 2, cursor: null, CancellationToken.None);

            Assert.HasCount(2, first.Entries);
            Assert.AreEqual("Feature A", first.Entries[0].Path);
            Assert.AreEqual("Feature A > Motivation", first.Entries[1].Path);
            Assert.IsTrue(first.IsPartial);
            Assert.IsNotNull(first.NextPageToken);
            Assert.AreEqual("test/csharplang", first.Source.Repo);

            var second = await service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 2, first.NextPageToken, CancellationToken.None);
            Assert.AreEqual("Feature A > Detailed design", second.Entries[0].Path);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GetOutlineAsync(
                "docs/proposal-b.md", "csharplang", limit: 2, first.NextPageToken, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncRejectsAPathThatEscapesTheSourceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            await Assert.ThrowsExactlyAsync<LanguageDocPathNotFoundException>(() => service.GetOutlineAsync(
                "../../etc/passwd", "csharplang", limit: 20, cursor: null, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<LanguageDocPathNotFoundException>(() => service.GetOutlineAsync(
                "docs/does-not-exist.md", "csharplang", limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncThrowsSourceNotSyncedWhenNeverSynced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, Path.Combine(root, "origin"), new string('a', 40));
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var exception = await Assert.ThrowsExactlyAsync<SourceNotSyncedException>(() => service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 20, cursor: null, CancellationToken.None));
            Assert.AreEqual("csharplang", exception.SourceName);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<LanguageDocsQueryService> CreateServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-b.md"), ProposalB);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new LanguageDocsQueryService(catalog, cache, synchronizer);
    }

    private static async Task WriteCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["csharplang"] = new
                {
                    repository = "test/csharplang",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
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
