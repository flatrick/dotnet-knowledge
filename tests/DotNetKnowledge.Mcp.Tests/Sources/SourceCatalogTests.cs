using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class SourceCatalogTests
{
    [TestMethod]
    public void CatalogRejectsPathsThatEscapeTheSourceCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sources.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                {
                  "schemaVersion": 1,
                  "sources": {
                    "../escape": {
                      "repository": "test/local",
                      "url": "C:/tmp/local",
                      "pin": "0123456789012345678901234567890123456789",
                      "head": "main",
                      "sparse": ["../outside"],
                      "purpose": "Test source."
                    }
                  }
                }
                """);

            var catalog = new SourceCatalog(path);

            Assert.ThrowsExactly<InvalidDataException>(() => _ = catalog.Sources);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CatalogCanLoadAnExplicitPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sources.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                {
                  "schemaVersion": 1,
                  "sources": {
                    "local": {
                      "repository": "test/local",
                      "url": "C:/tmp/local",
                      "pin": "0123456789012345678901234567890123456789",
                      "head": "main",
                      "sparse": ["docs"],
                      "purpose": "Test source."
                    }
                  }
                }
                """);

            var catalog = new SourceCatalog(path);

            Assert.AreEqual("test/local", catalog.Sources["local"].Repository);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CatalogRejectsNullSourceDefinitionAsInvalidData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sources.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """
                { "schemaVersion": 1, "sources": { "local": null } }
                """);

            var catalog = new SourceCatalog(path);

            Assert.ThrowsExactly<InvalidDataException>(() => _ = catalog.Sources);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void BundledCatalogCarriesRepositoryIdentityForProvenance()
    {
        var catalog = new SourceCatalog();

        Assert.AreEqual("dotnet/csharplang", catalog.Sources["csharplang"].Repository);
        Assert.AreEqual("dotnet/vblang", catalog.Sources["vblang"].Repository);
        Assert.AreEqual("dotnet/roslyn-api-docs", catalog.Sources["roslyn-api-docs"].Repository);
        Assert.AreEqual("dotnet/dotnet-api-docs", catalog.Sources["dotnet-api-docs"].Repository);
        Assert.AreEqual("dotnet/roslyn", catalog.Sources["roslyn-wiki"].Repository);
    }
}
