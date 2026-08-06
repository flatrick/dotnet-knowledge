using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;
using static DotNetKnowledge.Mcp.Tests.Features.ApiDocs.ApiDocsFixture;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiDocsToolTests
{
    private static readonly string[] ExpectedResolvedTypes = ["System.Widget"];

    [TestMethod]
    public async Task LookupApiNamesTheRequiredSyncWhenSourceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var catalog = new SourceCatalog();
            var cache = new SourceCache(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new ApiDocsQueryService(catalog, cache, synchronizer);

            var json = await ApiDocsTool.LookupApi(
                "Widget",
                service,
                CancellationToken.None,
                "dotnet-api-docs");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("source_not_synced", document.RootElement.GetProperty("error").GetString());
            Assert.AreEqual("dotnet-api-docs", document.RootElement.GetProperty("source").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "Call sync_source");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task LookupApiReturnsNotFoundAndNamesSearchApiWhenTypeDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var json = await ApiDocsTool.LookupApi(
                "System.MissingWidget",
                service,
                CancellationToken.None,
                "dotnet-api-docs");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("not_found", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "search_api");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupApiReturnsMemberNotFoundWithResolvedTypesWhenMemberDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var json = await ApiDocsTool.LookupApi(
                "Widget.NotAMember",
                service,
                CancellationToken.None,
                "dotnet-api-docs");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("member_not_found", document.RootElement.GetProperty("error").GetString());
            var resolvedTypes = document.RootElement.GetProperty("resolvedTypes")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray();
            CollectionAssert.AreEqual(ExpectedResolvedTypes, resolvedTypes);
            var message = document.RootElement.GetProperty("message").GetString();
            StringAssert.Contains(message, "lookup_api");
            StringAssert.Contains(message, "System.Widget");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupApiReturnsAnEmptyPageWhenACursorLandsExactlyAtTheEnd()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);
            var whole = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 100, cursor: null, CancellationToken.None);

            // Paging runs over one flat member sequence, with a placeholder slot for a type
            // carrying no members, so this is the offset one past the last page's last item.
            var end = whole.Matches.Sum(match => Math.Max(1, match.Members.Count));
            var pin = (await RunGitAsync(Path.Combine(root, "origin"), "rev-parse", "HEAD")).Trim();
            var atTheEnd = ApiDocsQueryService.EncodeCursor(
                "lookup", "Widget", end, [$"test/dotnet-api-docs@pinned@{pin}"]);

            var json = await ApiDocsTool.LookupApi(
                "Widget",
                service,
                CancellationToken.None,
                "dotnet-api-docs",
                cursor: atTheEnd);

            using var document = JsonDocument.Parse(json);
            Assert.IsFalse(
                document.RootElement.TryGetProperty("error", out var error),
                $"a valid cursor at the end of the result set is an empty page, not {error}.");
            Assert.IsEmpty(document.RootElement.GetProperty("matches").EnumerateArray().ToArray());
            Assert.IsFalse(document.RootElement.GetProperty("isPartial").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FindApiReferencesSaysWhetherAHitIsTheTypeItselfOrAParameterizationOfIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var json = await ApiDocsTool.FindApiReferences(
                "System.String",
                service,
                CancellationToken.None,
                source: "dotnet-api-docs",
                limit: 100);

            using var document = JsonDocument.Parse(json);
            var hits = document.RootElement.GetProperty("hits").EnumerateArray().ToArray();
            var bare = hits
                .Where(hit => hit.GetProperty("typeExpression").GetString() == "System.String")
                .ToArray();
            var compound = hits
                .Where(hit => hit.GetProperty("typeExpression").GetString() != "System.String")
                .ToArray();

            Assert.IsNotEmpty(bare);
            Assert.IsNotEmpty(compound);
            Assert.IsTrue(bare.All(hit => hit.GetProperty("isExact").GetBoolean()));
            Assert.IsFalse(compound.Any(hit => hit.GetProperty("isExact").GetBoolean()));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchApiSaysHowFarBelowTheNamedNamespaceEachMatchSits()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var json = await ApiDocsTool.SearchApi("System", service, CancellationToken.None, limit: 100);

            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.GetProperty("items")
                .EnumerateArray()
                .ToDictionary(item => item.GetProperty("name").GetString()!, item => item);

            // Both readings are returned, as they must be, but a caller wanting only what is
            // declared in System itself cannot otherwise express it.
            Assert.AreEqual(0, items["System.Widget"].GetProperty("namespaceDepth").GetInt32());
            Assert.AreEqual(1, items["System.Widgets.Gadget"].GetProperty("namespaceDepth").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupApiReturnsInvalidCursorForAMalformedCursor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);
            var malformedCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    "{\"Version\":1,\"Pattern\":\"Widget\",\"Offset\":0}"))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            var json = await ApiDocsTool.LookupApi(
                "Widget",
                service,
                CancellationToken.None,
                "dotnet-api-docs",
                cursor: malformedCursor);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_cursor", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

}
