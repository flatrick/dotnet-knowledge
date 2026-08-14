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
    public async Task LookupApiReturnsNotFoundWhenTypeDoesNotExist()
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
            StringAssert.Contains(
                document.RootElement.GetProperty("message").GetString(), "synchronized coverage");
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
                "Widget", "dotnet-api-docs", framework: null,
                limit: 100, cursor: null, CancellationToken.None);

            // Paging runs over one flat member sequence, with a placeholder slot for a type
            // carrying no members, so this is the offset one past the last page's last item.
            var end = whole.Matches.Sum(match => Math.Max(1, match.Members.Count));
            var atTheEnd = ApiDocsQueryService.EncodeCursor(
                "lookup",
                ApiDocsQueryService.RequestScope("Widget", "dotnet-api-docs", whole.EffectiveFramework),
                end,
                whole.SearchedSources.Select(source => source.RevisionKey).ToArray());

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
    public async Task FindApiReferencesCarriesTheResolvedAttributeTypeAndNamesAnExcludedSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateAttributeSiblingServiceAsync(root);

            var found = await ApiDocsTool.FindApiReferences(
                "System.WidgetSealAttribute",
                service,
                CancellationToken.None,
                kind: ApiReferenceKind.Attribute,
                source: "dotnet-api-docs",
                limit: 100);

            using var foundDocument = JsonDocument.Parse(found);
            var hit = foundDocument.RootElement.GetProperty("hits").EnumerateArray().Single();
            Assert.AreEqual("[System.WidgetSeal]", hit.GetProperty("typeExpression").GetString());
            Assert.AreEqual("System.WidgetSealAttribute", hit.GetProperty("attributeType").GetString());

            // The note exists so an exclusion is never silent, so it has to survive serialization.
            var excluded = await ApiDocsTool.FindApiReferences(
                "System.WidgetTrait",
                service,
                CancellationToken.None,
                source: "dotnet-api-docs",
                limit: 100);

            using var excludedDocument = JsonDocument.Parse(excluded);
            var note = excludedDocument.RootElement.GetProperty("note");
            Assert.AreEqual("System.WidgetTraitAttribute", note.GetProperty("siblingType").GetString());
            Assert.AreEqual(1, note.GetProperty("attributeApplications").GetInt32());
            StringAssert.Contains(note.GetProperty("remedy").GetString(), "find_api_references");

            // Absent rather than null on the kinds and the queries that have nothing to say, which
            // is what keeps the field free on every other response.
            var parameterHit = excludedDocument.RootElement.GetProperty("hits")
                .EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == ApiReferenceKind.Parameter);
            Assert.IsFalse(parameterHit.TryGetProperty("attributeType", out _));
            Assert.IsFalse(foundDocument.RootElement.TryGetProperty("note", out _));
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
    public async Task EveryApiToolReportsTheFrameworkItActuallyQueried()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);

            var responses = new[]
            {
                await ApiDocsTool.LookupApi(
                    "LegacyWidget", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net8.0"),
                await ApiDocsTool.SearchApi(
                    "LegacyWidget", service, CancellationToken.None, framework: "net8.0"),
                await ApiDocsTool.SearchApiText(
                    "widget", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net8.0"),
                await ApiDocsTool.FindApiReferences(
                    "System.WidgetTraitAttribute", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net8.0"),
            };

            foreach (var response in responses)
            {
                using var document = JsonDocument.Parse(response);
                Assert.IsFalse(
                    document.RootElement.TryGetProperty("error", out var error),
                    $"an available framework is not an error, but {error} was returned.");
                Assert.AreEqual(
                    "net8.0", document.RootElement.GetProperty("effectiveFramework").GetString());
                Assert.AreEqual(
                    "net10.0", document.RootElement.GetProperty("defaultFramework").GetString());
                CollectionAssert.AreEqual(
                    PackageFrameworks,
                    document.RootElement.GetProperty("availableFrameworks")
                        .EnumerateArray()
                        .Select(item => item.GetString())
                        .ToArray());
            }
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task EveryApiToolReportsAnUnavailableFrameworkAsAStructuredFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);

            var responses = new[]
            {
                await ApiDocsTool.LookupApi(
                    "Widget", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net7.0"),
                await ApiDocsTool.SearchApi(
                    "Widget", service, CancellationToken.None, framework: "net7.0"),
                await ApiDocsTool.SearchApiText(
                    "widget", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net7.0"),
                await ApiDocsTool.FindApiReferences(
                    "System.String", service, CancellationToken.None,
                    source: "roslyn-api-docs", framework: "net7.0"),
            };

            foreach (var response in responses)
            {
                using var document = JsonDocument.Parse(response);
                Assert.AreEqual(
                    "framework_not_available", document.RootElement.GetProperty("error").GetString());
                Assert.AreEqual(
                    "net7.0", document.RootElement.GetProperty("requestedFramework").GetString());
                Assert.AreEqual(
                    "net10.0", document.RootElement.GetProperty("defaultFramework").GetString());
                CollectionAssert.AreEqual(
                    PackageFrameworks,
                    document.RootElement.GetProperty("availableFrameworks")
                        .EnumerateArray()
                        .Select(item => item.GetString())
                        .ToArray());
            }

            // A framework-neutral source is a caller mistake about the source, not about the
            // framework, so it keeps the generic remedy rather than listing package frameworks.
            var neutral = await ApiDocsTool.LookupApi(
                "Widget", service, CancellationToken.None,
                source: "dotnet-api-docs", framework: "net8.0");
            using var neutralDocument = JsonDocument.Parse(neutral);
            Assert.AreEqual(
                "invalid_request", neutralDocument.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupApiReportsAbsenceAgainstStatedCoverageRatherThanCompleteness()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);

            var json = await ApiDocsTool.LookupApi(
                "System.MissingWidget", service, CancellationToken.None, source: "roslyn-api-docs");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("not_found", document.RootElement.GetProperty("error").GetString());
            var message = document.RootElement.GetProperty("message").GetString();
            StringAssert.Contains(message, "stated synchronized coverage");
            // search_api enumerates the same coverage this lookup already searched, so sending the
            // caller there promises a completeness neither tool has.
            Assert.DoesNotContain("search_api", message);
            Assert.AreEqual(
                "net10.0", document.RootElement.GetProperty("effectiveFramework").GetString());
            Assert.AreEqual(
                "net10.0", document.RootElement.GetProperty("defaultFramework").GetString());
            CollectionAssert.AreEqual(
                PackageFrameworks,
                document.RootElement.GetProperty("availableFrameworks")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .ToArray());
            var searchedSources = document.RootElement.GetProperty("searchedSources")
                .EnumerateArray()
                .Select(item => item.GetProperty("kind").GetString())
                .ToArray();
            CollectionAssert.Contains(searchedSources, "git");
            CollectionAssert.Contains(searchedSources, "nuget");
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
