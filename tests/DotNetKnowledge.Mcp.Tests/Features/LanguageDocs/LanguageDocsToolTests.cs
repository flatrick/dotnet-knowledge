using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.LanguageDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.LanguageDocs;

[TestClass]
public sealed class LanguageDocsToolTests
{
    private const string ProposalA =
        "# Feature A\n\n## Motivation\n\nSome motivating prose.\n";

    [TestMethod]
    public async Task GetLanguageDocOutlineNamesTheRequiredSyncWhenSourceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var catalog = new SourceCatalog();
            var cache = new SourceCache(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
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
    public async Task GetLanguageDocOutlineReturnsPathNotFoundForAMissingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
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
    public async Task GetLanguageDocOutlineReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
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
    public async Task SearchLanguageDocsReturnsInvalidRegexForAnUnsupportedConstruct()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.SearchLanguageDocs(
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
    public async Task SearchLanguageDocsReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.SearchLanguageDocs(
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
    public async Task GetLanguageDocReturnsSectionNotFoundNamingTheOutlineTool()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None, section: "No Such Section");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("section_not_found", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "get_language_doc_outline");
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
