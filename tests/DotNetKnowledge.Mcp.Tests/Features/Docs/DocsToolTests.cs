using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.Docs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.Docs;

[TestClass]
public sealed class DocsToolTests
{
    private const string ProposalA =
        "# Feature A\n\n## Motivation\n\nSome motivating prose.\n";

    [TestMethod]
    public async Task GetDocOutlineNamesTheRequiredSyncWhenSourceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var catalog = new SourceCatalog();
            var cache = new SourceCache(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new DocsQueryService(catalog, cache, synchronizer);

            var json = await DocsTool.GetDocOutline(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("source_not_synced", document.RootElement.GetProperty("error").GetString());
            Assert.AreEqual("csharplang", document.RootElement.GetProperty("source").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "sync_source");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetDocOutlineReturnsPathNotFoundForAMissingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.GetDocOutline(
                "docs/missing.md", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("path_not_found", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocOutlineReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.GetDocOutline(
                "docs/proposal-a.md", "not-a-real-source", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_request", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchDocsReturnsInvalidRegexForAnUnsupportedConstruct()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.SearchDocs(
                @"(\w+)\s+\1", service, CancellationToken.None, regex: true);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_regex", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchDocsReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.SearchDocs(
                "prose", service, CancellationToken.None, source: "not-a-real-source");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_request", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocReturnsSectionNotFoundNamingTheOutlineTool()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None, section: "No Such Section");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("section_not_found", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "get_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocReturnsNormalizationNoteAsCamelCaseJsonWhenTheFallbackFires()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None,
                section: "Feature A &gt; Motivation");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(
                "Feature A > Motivation", document.RootElement.GetProperty("section").GetString());
            StringAssert.Contains(
                document.RootElement.GetProperty("normalizationNote").GetProperty("message").GetString(),
                "Feature A > Motivation");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<DocsQueryService> CreateServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new DocsQueryService(catalog, cache, synchronizer);
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
                    markdown = true,
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
