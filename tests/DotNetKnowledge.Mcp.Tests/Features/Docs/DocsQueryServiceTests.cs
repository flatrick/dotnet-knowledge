using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.Docs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.Docs;

[TestClass]
public sealed class DocsQueryServiceTests
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

    // A Microsoft Learn article. Every NuGet document opens this way, and 451 of the 463 under
    // docs/ do. Front matter must not become a heading, and the section path the outline issues
    // must be the one get_doc accepts back.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "ms.topic: concept-article\n" +
        "---\n" +
        "\n" +
        "# PackageReference in project files\n" +
        "\n" +
        "Intro prose about package references.\n" +
        "\n" +
        "## Project type support\n" +
        "\n" +
        "Prose about which project types support it.\n";

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

            await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetOutlineAsync(
                "../../etc/passwd", "csharplang", limit: 20, cursor: null, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetOutlineAsync(
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
            var service = new DocsQueryService(catalog, cache, synchronizer);

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
    public async Task SearchAsyncFindsAnAnchoredRegexMatchNotOnTheFilesFirstLine()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            // "^## " only matches offset 0 of whatever string it's tested against (no
            // RegexOptions.Multiline). The real per-line matcher (MarkdownLineSearch.Search) tests
            // it against each line individually, so it finds the heading on line 7. A prefilter
            // that instead tests the pattern once against the whole file text would see "^"
            // anchored to the very first line ("# Feature E") and wrongly conclude nothing matches,
            // silently skipping the file before MarkdownOutline.Extract or MarkdownLineSearch.Search
            // ever run.
            const string document =
                "# Feature E\n" +
                "\n" +
                "Some prose before any level-2 heading.\n" +
                "\n" +
                "## Detailed design\n" +
                "\n" +
                "More prose.\n";
            var service = await CreateServiceWithDocumentAsync(root, "proposal-e.md", document);

            var result = await service.SearchAsync(
                "^## ", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(1, result.Hits);
            Assert.AreEqual("docs/proposal-e.md", result.Hits[0].Path);
            Assert.AreEqual(5, result.Hits[0].Line);
            Assert.AreEqual("## Detailed design", result.Hits[0].Text);
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
            var service = new DocsQueryService(catalog, cache, synchronizer);

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
            var service = new DocsQueryService(catalog, cache, synchronizer);

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

            await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetOutlineAsync(
                "docs/notes.txt", "csharplang", limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineOmitsYamlFrontMatterFromLearnArticles()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var outline = await service.GetOutlineAsync(
                "docs/article.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.HasCount(2, outline.Entries);
            Assert.AreEqual("PackageReference in project files", outline.Entries[0].Path);
            Assert.AreEqual(1, outline.Entries[0].Level);
            Assert.AreEqual(
                "PackageReference in project files > Project type support",
                outline.Entries[1].Path);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAcceptsASectionPathIssuedForALearnArticle()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var outline = await service.GetOutlineAsync(
                "docs/article.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);
            var section = outline.Entries[1].Path;

            // Under the pre-front-matter parse this is "PackageReference in project files" — the
            // phantom heading pushes the real H1 into the H2's place — so this is what makes the
            // round trip a regression test rather than a smoke test.
            Assert.AreEqual("PackageReference in project files > Project type support", section);

            // The round trip is the contract: whatever the outline issued, get_doc takes verbatim.
            var content = await service.GetDocAsync(
                "docs/article.md", "csharplang", section, limit: 8000, cursor: null, CancellationToken.None);

            StringAssert.Contains(content.Text, "## Project type support");
            StringAssert.Contains(content.Text, "Prose about which project types support it.");
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.author"));
            Assert.IsFalse(content.IsPartial);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocOmitsFrontMatterFromAWholeDocumentFetch()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var content = await service.GetDocAsync(
                "docs/article.md", "csharplang", section: null, limit: 8000, cursor: null, CancellationToken.None);

            // The payload begins at the real heading, and StartLine names the line it came from -
            // so a returned line number still points at the same line in the file on disk.
            StringAssert.StartsWith(content.Text, "# PackageReference in project files");
            Assert.AreEqual(8, content.StartLine);
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.author"));
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.date"));
            Assert.IsFalse(content.IsPartial);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchReturnsNoHitInsideFrontMatterButStillFindsTheBody()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            // "ms.author" appears only as a front-matter key. The file passes ReadSearchSource's
            // prefilter, so this also proves the per-line skip runs after that prefilter admits it.
            var metadata = await service.SearchAsync(
                "ms.author", regex: false, "csharplang", limit: 20, cursor: null, CancellationToken.None);

            Assert.IsEmpty(metadata.Hits);

            // A body term still matches, at its real file line.
            var body = await service.SearchAsync(
                "PackageReference", regex: false, "csharplang", limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(1, body.Hits);
            Assert.AreEqual(8, body.Hits[0].Line);
            Assert.AreEqual("docs/article.md", body.Hits[0].Path);
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

            var exception = await Assert.ThrowsExactlyAsync<DocSectionNotFoundException>(() => service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "No Such Section", limit: 8000, cursor: null,
                CancellationToken.None));
            StringAssert.Contains(exception.Message, "get_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncAcceptsAnHtmlEncodedSectionSeparatorAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A &gt; Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("Feature A > Motivation", result.Section);
            StringAssert.Contains(result.Text, "Some motivating prose about feature A.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "Feature A &gt; Motivation");
            StringAssert.Contains(result.NormalizationNote!.Message, "Feature A > Motivation");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncDoesNotReportNormalizationWhenTheLiteralSectionAlreadyMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A > Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncStillThrowsWhenNormalizationDoesNotProduceAMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var exception = await Assert.ThrowsExactlyAsync<DocSectionNotFoundException>(() => service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "No Such Section &gt; At All", limit: 8000, cursor: null,
                CancellationToken.None));
            StringAssert.Contains(exception.Message, "get_doc_outline");
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

    [TestMethod]
    public async Task GetDocAsyncAcceptsAnHtmlEncodedPathAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\nProse about A and B.\n");

            var result = await service.GetDocAsync(
                "docs/a&amp;b.md", "csharplang", section: null, limit: 8000, cursor: null, CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            StringAssert.Contains(result.Text, "Prose about A and B.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&amp;b.md");
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&b.md");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncAcceptsAnHtmlEncodedPathAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\n## Detail\n\nMore prose.\n");

            var result = await service.GetOutlineAsync(
                "docs/a&amp;b.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            Assert.IsNotNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncDoesNotReportNormalizationWhenTheLiteralPathAlreadyMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncStillThrowsPathNotFoundWhenNormalizationDoesNotResolveIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            // Both the literal path and its decoded form ("docs/does&not-exist.md") are misses, so
            // the retry inside ReadDocumentAsync also fails. The propagated exception must still
            // name exactly what the caller sent - the internally-decoded guess that also missed is
            // an implementation detail, not something to surface - mirroring how
            // DocSectionNotFoundException reports the caller's raw `section`, not `normalizedSection`.
            var exception = await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetDocAsync(
                "docs/does&amp;not-exist.md", "csharplang", section: null, limit: 8000, cursor: null,
                CancellationToken.None));

            Assert.AreEqual("docs/does&amp;not-exist.md", exception.Path);
            StringAssert.Contains(exception.Message, "docs/does&amp;not-exist.md");
            Assert.IsFalse(exception.Message.Contains("does&not-exist.md"));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncStillThrowsPathNotFoundWhenNormalizationDecodesToARejectedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            // "&#0;" decodes to a literal NUL character, which Path.GetFullPath rejects with
            // ArgumentException rather than the DocPathNotFoundException the retry's inner catch was
            // written to expect. The propagated exception must still be a DocPathNotFoundException
            // naming exactly what the caller sent - never the ArgumentException, and never the
            // internally-decoded guess that also failed.
            var exception = await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetDocAsync(
                "docs/x&#0;.md", "csharplang", section: null, limit: 8000, cursor: null,
                CancellationToken.None));

            Assert.AreEqual("docs/x&#0;.md", exception.Path);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncCombinesPathAndSectionNormalizationNotesWhenBothFire()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\n## Detail\n\nMore prose.\n");

            var result = await service.GetDocAsync(
                "docs/a&amp;b.md", "csharplang", "A and B &gt; Detail", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            Assert.AreEqual("A and B > Detail", result.Section);
            StringAssert.Contains(result.Text, "More prose.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&amp;b.md");
            StringAssert.Contains(result.NormalizationNote!.Message, "A and B &gt; Detail");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncAcceptsAnHtmlEncodedQueryAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "proposal-f.md", "# Feature F\n\nA constraint written as T > U compares the two.\n");

            var result = await service.SearchAsync(
                "T &gt; U", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(1, result.Hits);
            StringAssert.Contains(result.Hits[0].Text, "T > U");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "T &gt; U");
            StringAssert.Contains(result.NormalizationNote!.Message, "T > U");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncDoesNotNormalizeARegexQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "proposal-f.md", "# Feature F\n\nA constraint written as T > U compares the two.\n");

            var result = await service.SearchAsync(
                "T &gt; U", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.IsEmpty(result.Hits);
            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncDoesNotReportNormalizationWhenTheLiteralQueryAlreadyMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.SearchAsync(
                "FeatureA", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncDoesNotRetryWithABlankNormalizedQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            // "&#32;" is a numeric HTML entity for a plain space: it is not whitespace as written, so
            // it passes ArgumentException.ThrowIfNullOrWhiteSpace, misses literally, and normalizes to
            // " " - which the retry must refuse to search with, rather than scanning every markdown
            // file in every configured source for a bare space.
            var result = await service.SearchAsync(
                "&#32;", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.IsEmpty(result.Hits);
            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<DocsQueryService> CreateServiceWithDocumentAsync(
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
        return new DocsQueryService(catalog, cache, synchronizer);
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
