using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiDocsToolTests
{
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
}
