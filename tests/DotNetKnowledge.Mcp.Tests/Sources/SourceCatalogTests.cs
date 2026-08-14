using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class SourceCatalogTests
{
    [TestMethod]
    public void BundledRoslynSourcePinsTheMsbuildApiPackage()
    {
        var packages = new SourceCatalog().Sources["roslyn-api-docs"].ApiPackages;

        Assert.HasCount(1, packages!);
        var package = packages![0];
        Assert.AreEqual("Microsoft.CodeAnalysis.Workspaces.MSBuild", package.PackageId);
        Assert.AreEqual("5.3.0", package.Version);
        Assert.AreEqual("net10.0", package.DefaultFramework);
    }

    // Every cataloged package must carry the cohort version the checkout's manifest names, which is
    // what stops the catalog drifting from the documentation it is joined to. It is also why a
    // package outside the Roslyn cohort cannot be added here at all -- see
    // docs/backlog/api-packages-are-limited-to-the-roslyn-cohort.md.
    [TestMethod]
    public void EveryCatalogedApiPackageSharesOneVersion()
    {
        var packages = new SourceCatalog().Sources["roslyn-api-docs"].ApiPackages!;

        Assert.HasCount(1, packages.Select(item => item.Version).Distinct(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void BacklogNoLongerCarriesTheApiCoverageItemThePackageSupplementCloses()
    {
        var index = File.ReadAllText(GetFixturePath("BacklogIndexPath"));

        Assert.DoesNotContain("api-coverage-stops-at-the-documented-package-set.md", index);
    }

    [TestMethod]
    [DataRow("packageId", "")]
    [DataRow("packageId", "//evil.test/package")]
    [DataRow("packageId", "../escape")]
    [DataRow("packageId", "..")]
    [DataRow("assemblyName", "path/to/assembly")]
    [DataRow("feed", "http://api.nuget.org/v3/index.json")]
    [DataRow("version", "latest")]
    [DataRow("sha512", "not-a-64-byte-base64-hash")]
    [DataRow("defaultFramework", "")]
    public void CatalogRejectsInvalidApiPackageDefinitions(string field, string invalidValue)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sources.json");

        try
        {
            Directory.CreateDirectory(directory);
            var package = new Dictionary<string, string>
            {
                ["packageId"] = "Test.Package",
                ["assemblyName"] = "Test.Package",
                ["feed"] = "https://api.nuget.org/v3/index.json",
                ["version"] = "1.2.3",
                ["sha512"] = Convert.ToBase64String(new byte[64]),
                ["defaultFramework"] = "net10.0",
            };
            package[field] = invalidValue;
            File.WriteAllText(path, CreateSourcesJson($"[{JsonSerializer.Serialize(package)}]"));

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
    public void CatalogRejectsDuplicateApiPackageIdsIgnoringCase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "sources.json");

        try
        {
            Directory.CreateDirectory(directory);
            var package = "{\"packageId\":\"Test.Package\",\"assemblyName\":\"Test.Package\",\"feed\":\"https://api.nuget.org/v3/index.json\",\"version\":\"1.2.3\",\"sha512\":\"" + Convert.ToBase64String(new byte[64]) + "\",\"defaultFramework\":\"net10.0\"}";
            var duplicate = package.Replace("Test.Package", "test.package", StringComparison.Ordinal);
            File.WriteAllText(path, CreateSourcesJson($"[{package},{duplicate}]"));

            var catalog = new SourceCatalog(path);

            Assert.ThrowsExactly<InvalidDataException>(() => _ = catalog.Sources);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateSourcesJson(string apiPackages) => $$"""
        {
          "schemaVersion": 1,
          "sources": {
            "local": {
              "repository": "test/local",
              "url": "C:/tmp/local",
              "pin": "0123456789012345678901234567890123456789",
              "head": "main",
              "sparse": ["docs"],
              "purpose": "Test source.",
              "apiPackages": {{apiPackages}}
            }
          }
        }
        """;

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
        Assert.AreEqual("NuGet/docs.microsoft.com-nuget", catalog.Sources["nuget-docs"].Repository);
        Assert.IsTrue(catalog.Sources["nuget-docs"].Markdown);
    }

    private static string GetFixturePath(string key) => typeof(SourceCatalogTests).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value
        ?? throw new InvalidOperationException($"Missing test fixture metadata '{key}'.");
}
