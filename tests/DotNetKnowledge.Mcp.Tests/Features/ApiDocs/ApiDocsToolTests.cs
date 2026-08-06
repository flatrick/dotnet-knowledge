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
