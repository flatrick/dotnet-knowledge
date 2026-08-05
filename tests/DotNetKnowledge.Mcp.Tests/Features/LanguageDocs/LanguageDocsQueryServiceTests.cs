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

    private static readonly string[] ExpectedRegexHitPaths = ["docs/proposal-a.md", "docs/proposal-b.md"];

    private static readonly string[] ExpectedMultiSourceRepos = ["test/csharplang", "test/vblang"];

    // A section long enough (well over the 1000-char minimum limit) that a single call cannot
    // return it in one page, with no fenced code or tables so pagination/cursor mechanics are
    // exercised in isolation from the atomic-block extension logic.
    private static readonly string FeatureCDocument = BuildFeatureCDocument();

    // A section shaped so the pager's naive per-line budget accumulation lands inside the fenced
    // code block (after the opening ``` but before the closing one), and only the atomic-block
    // extension logic in MarkdownPager.Page saves it from splitting the fence.
    private static readonly string FeatureDDocument = BuildFeatureDDocument();

    private static string BuildFeatureCDocument()
    {
        var filler = string.Concat(Enumerable.Range(1, 20).Select(i =>
            $"This is filler prose line number {i,2} used only to pad the character budget for a pagination test.\n"));

        return
            "# Feature C\n" +
            "\n" +
            "## Long Section\n" +
            "\n" +
            filler +
            "\n" +
            "## Trailing Section\n" +
            "\n" +
            "Trailing text so Long Section has a bounded EndLine.\n";
    }

    private static string BuildFeatureDDocument()
    {
        var prose = string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"Prose padding line {i,2} before the fence starts, kept short-ish on purpose here.\n"));
        var fenceBody = string.Concat(Enumerable.Range(1, 6).Select(i =>
            $"line {i} of fenced content padding out the block\n"));

        return
            "# Feature D\n" +
            "\n" +
            "## Boundary Section\n" +
            "\n" +
            prose +
            "\n" +
            "```text\n" +
            fenceBody +
            "```\n" +
            "\n" +
            "More text after the fence to prove the page still reaches past it.\n";
    }

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

    [TestMethod]
    public async Task SearchAsyncMatchesLiteralAndRegexAcrossFilesAndOrdersDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var literal = await service.SearchAsync(
                "FeatureA", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);
            Assert.HasCount(1, literal.Hits);
            Assert.AreEqual("docs/proposal-b.md", literal.Hits[0].Path);
            Assert.AreEqual("Feature B > Summary", literal.Hits[0].SectionPath);

            var regex = await service.SearchAsync(
                "Feature [AB]", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None);
            Assert.HasCount(2, regex.Hits);
            CollectionAssert.AreEqual(
                ExpectedRegexHitPaths,
                regex.Hits.Select(hit => hit.Path).ToArray());

            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.SearchAsync(
                @"(\w+)\s+\1", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncSearchesEveryConfiguredSourceAndOrdersHitsByRepoWhenPathsTie()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var csharplangRepository = Path.Combine(root, "csharplang-origin");
            var vblangRepository = Path.Combine(root, "vblang-origin");

            var csharplangPin = await InitDocsRepositoryAsync(
                csharplangRepository, "shared.md", "# Shared\n\nCSharp note about SharedTopic.\n");
            var vblangPin = await InitDocsRepositoryAsync(
                vblangRepository, "shared.md", "# Shared\n\nVB note about SharedTopic.\n");

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteTwoSourceCatalogAsync(
                catalogPath, csharplangRepository, csharplangPin, vblangRepository, vblangPin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
            await synchronizer.SyncAsync("vblang", requestedRef: null, CancellationToken.None);
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var result = await service.SearchAsync(
                "SharedTopic", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(2, result.Hits);
            Assert.AreEqual("docs/shared.md", result.Hits[0].Path);
            Assert.AreEqual("docs/shared.md", result.Hits[1].Path);
            CollectionAssert.AreEqual(
                ExpectedMultiSourceRepos,
                result.Hits.Select(hit => hit.Source.Repo).ToArray());

            CollectionAssert.AreEquivalent(
                ExpectedMultiSourceRepos,
                result.SearchedSources.Select(source => source.Repo).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncOnlySearchesMarkdownFlaggedSourcesAndRejectsANonMarkdownSourceName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var csharplangRepository = Path.Combine(root, "csharplang-origin");
            var vblangRepository = Path.Combine(root, "vblang-origin");
            var apiRepository = Path.Combine(root, "api-origin");

            var csharplangPin = await InitDocsRepositoryAsync(
                csharplangRepository, "shared.md", "# Shared\n\nCSharp note about SharedTopic.\n");
            var vblangPin = await InitDocsRepositoryAsync(
                vblangRepository, "shared.md", "# Shared\n\nVB note about SharedTopic.\n");
            var apiPin = await InitDocsRepositoryAsync(
                apiRepository, "shared.md", "# Shared\n\nAPI note about SharedTopic.\n");

            var catalogPath = Path.Combine(root, "sources.json");
            await WriteThreeSourceCatalogAsync(
                catalogPath, csharplangRepository, csharplangPin, vblangRepository, vblangPin, apiRepository, apiPin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
            await synchronizer.SyncAsync("vblang", requestedRef: null, CancellationToken.None);
            // "roslyn-api-docs" is deliberately left unsynced: rejecting it as a non-markdown
            // source must happen at source validation, before sync state is ever consulted.
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var result = await service.SearchAsync(
                "SharedTopic", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(2, result.Hits);
            CollectionAssert.AreEquivalent(
                ExpectedMultiSourceRepos,
                result.SearchedSources.Select(source => source.Repo).ToArray());

            var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "SharedTopic", regex: false, source: "roslyn-api-docs", limit: 20, cursor: null,
                CancellationToken.None));
            Assert.AreEqual("source", exception.ParamName);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncRejectsANonMarkdownFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "notes.txt", "Not markdown at all.\n");

            await Assert.ThrowsExactlyAsync<LanguageDocPathNotFoundException>(() => service.GetOutlineAsync(
                "docs/notes.txt", "csharplang", limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncReturnsAWholeSectionAndRejectsAnUnknownSection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var section = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A > Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("Feature A > Motivation", section.Section);
            StringAssert.Contains(section.Text, "Some motivating prose about feature A.");
            Assert.IsFalse(section.Text.Contains("Detailed design"));
            Assert.IsFalse(section.IsPartial);

            var exception = await Assert.ThrowsExactlyAsync<LanguageDocSectionNotFoundException>(() => service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "No Such Section", limit: 8000, cursor: null,
                CancellationToken.None));
            StringAssert.Contains(exception.Message, "get_language_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncPagesByCharacterBudgetWithoutSplittingAFencedCodeBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "proposal-d.md", FeatureDDocument);

            // Twelve prose lines exhaust most of a 1000-char budget before the fence even starts,
            // so the naive per-line accumulation breaks right after the opening ``` marker (line
            // 18 of the fixed fixture) - inside the fence, not past it. Only the atomic-block
            // extension in MarkdownPager.Page can carry the page out to the closing marker (line
            // 25); without it this test fails, because the naive break leaves the fence unclosed.
            var page = await service.GetDocAsync(
                "docs/proposal-d.md", "csharplang", "Feature D > Boundary Section", limit: 1000, cursor: null,
                CancellationToken.None);

            StringAssert.Contains(page.Text, "```text");
            StringAssert.Contains(page.Text, "line 1 of fenced content padding out the block");
            StringAssert.Contains(page.Text, "line 6 of fenced content padding out the block");
            Assert.IsTrue(page.Text.EndsWith("```", StringComparison.Ordinal));
            Assert.AreEqual(25, page.EndLine);
            Assert.IsFalse(page.Text.Contains("More text after the fence"));
            Assert.IsTrue(page.IsPartial);
            Assert.IsNotNull(page.NextPageToken);

            var second = await service.GetDocAsync(
                "docs/proposal-d.md", "csharplang", "Feature D > Boundary Section", limit: 1000, page.NextPageToken,
                CancellationToken.None);

            Assert.AreEqual(page.EndLine + 1, second.StartLine);
            StringAssert.Contains(second.Text, "More text after the fence to prove the page still reaches past it.");
            Assert.IsFalse(second.IsPartial);
            Assert.IsNull(second.NextPageToken);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncPaginatesAcrossMultipleCallsWithACursor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "proposal-c.md", FeatureCDocument);

            var full = await service.GetDocAsync(
                "docs/proposal-c.md", "csharplang", "Feature C > Long Section", limit: 50000, cursor: null,
                CancellationToken.None);
            Assert.IsFalse(full.IsPartial);

            var first = await service.GetDocAsync(
                "docs/proposal-c.md", "csharplang", "Feature C > Long Section", limit: 1000, cursor: null,
                CancellationToken.None);
            Assert.IsTrue(first.IsPartial);
            Assert.IsNotNull(first.NextPageToken);

            var second = await service.GetDocAsync(
                "docs/proposal-c.md", "csharplang", "Feature C > Long Section", limit: 1000, first.NextPageToken,
                CancellationToken.None);
            Assert.IsFalse(second.IsPartial);
            Assert.IsNull(second.NextPageToken);

            Assert.AreEqual(first.EndLine + 1, second.StartLine);
            Assert.AreEqual(full.Text, first.Text + "\n" + second.Text);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<LanguageDocsQueryService> CreateServiceWithDocumentAsync(
        string root, string fileName, string content)
    {
        var repository = Path.Combine(root, "origin");
        var pin = await InitDocsRepositoryAsync(repository, fileName, content);
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new LanguageDocsQueryService(catalog, cache, synchronizer);
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
                    markdown = true,
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> InitDocsRepositoryAsync(string repository, string fileName, string content)
    {
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, fileName), content);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        return (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
    }

    private static async Task WriteTwoSourceCatalogAsync(
        string path,
        string csharplangRepository,
        string csharplangPin,
        string vblangRepository,
        string vblangPin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["csharplang"] = new
                {
                    repository = "test/csharplang",
                    url = csharplangRepository,
                    pin = csharplangPin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                    markdown = true,
                },
                ["vblang"] = new
                {
                    repository = "test/vblang",
                    url = vblangRepository,
                    pin = vblangPin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                    markdown = true,
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task WriteThreeSourceCatalogAsync(
        string path,
        string csharplangRepository,
        string csharplangPin,
        string vblangRepository,
        string vblangPin,
        string apiRepository,
        string apiPin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["csharplang"] = new
                {
                    repository = "test/csharplang",
                    url = csharplangRepository,
                    pin = csharplangPin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                    markdown = true,
                },
                ["vblang"] = new
                {
                    repository = "test/vblang",
                    url = vblangRepository,
                    pin = vblangPin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                    markdown = true,
                },
                // Simulates roslyn-api-docs/dotnet-api-docs: declared, but with no "markdown" field
                // at all, so it must default to false and stay unreachable by these tools.
                ["roslyn-api-docs"] = new
                {
                    repository = "test/roslyn-api-docs",
                    url = apiRepository,
                    pin = apiPin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test API docs.",
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
