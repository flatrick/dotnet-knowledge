using System.Text.Json;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class RoslynPackageCohortTests
{
    [TestMethod]
    public void ReadSelectsTheNumericMaximumManifestVersion()
    {
        using var fixture = new CohortFixture();
        fixture.WriteManifest("5.2.0", "5.2.0");
        fixture.WriteManifest("5.3.0", "5.3.0");

        Assert.AreEqual("5.3.0", RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    [TestMethod]
    public void ReadReturnsTheInternallyAgreedPackageVersionWhenItDiffersFromTheManifestVersion()
    {
        using var fixture = new CohortFixture();
        fixture.WriteManifest("4.9.0", "4.9.2", "4.9.2");

        Assert.AreEqual("4.9.2", RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    [TestMethod]
    public void ReadRequiresEveryPackageInTheSelectedManifestToUseOneVersion()
    {
        using var fixture = new CohortFixture();
        fixture.WriteManifest("5.3.0", "5.3.0", "5.2.0");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    [TestMethod]
    public void ReadRequiresThePinnedCatalogToMatchTheSelectedCohort()
    {
        using var fixture = new CohortFixture();
        fixture.WriteManifest("4.9.0", "4.9.2");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [Package("4.9.0")], pinned: true));
        Assert.AreEqual(
            "4.9.2",
            RoslynPackageCohort.Read(fixture.Repository, [Package("4.9.2")], pinned: true));
    }

    [TestMethod]
    public void ReadRejectsMissingOrMalformedInternalPackageVersions()
    {
        using var fixture = new CohortFixture();
        fixture.WriteRaw(
            "roslyn-dotnet-5.3.0.json",
            "{\"roslyn-dotnet-5.3.0\":{\"Package\":{\"Name\":\"Package\"}}}");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));

        fixture.WriteManifest("5.3.0", "not-a-version");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    [TestMethod]
    public void ReadRejectsMissingOrMalformedManifests()
    {
        using var fixture = new CohortFixture();
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));

        fixture.WriteRaw("roslyn-dotnet-not-a-version.json", "{}");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));

        fixture.WriteRaw("roslyn-dotnet-5.3.0.json", "{ not-json }");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    [TestMethod]
    public void ReadRejectsASelectedManifestWithoutPackageEntries()
    {
        using var fixture = new CohortFixture();
        fixture.WriteRaw("roslyn-dotnet-5.3.0.json", "{\"roslyn-dotnet-5.3.0\":{}}");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            RoslynPackageCohort.Read(fixture.Repository, [], pinned: false));
    }

    private static ApiPackageDefinition Package(string version) => new(
        "Microsoft.CodeAnalysis.Workspaces.MSBuild",
        "Microsoft.CodeAnalysis.Workspaces.MSBuild",
        "https://api.nuget.org/v3/index.json",
        version,
        Convert.ToBase64String(new byte[64]),
        "net10.0");

    private sealed class CohortFixture : IDisposable
    {
        private readonly string manifests;

        public CohortFixture()
        {
            Repository = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-cohort-{Guid.NewGuid():N}");
            manifests = Path.Combine(Repository, "dotnet", "xml", "PackageInformation");
            Directory.CreateDirectory(manifests);
        }

        public string Repository { get; }

        public void WriteManifest(string fileVersion, params string[] packageVersions)
        {
            var packages = packageVersions
                .Select((version, index) => new KeyValuePair<string, object>(
                    $"Microsoft.CodeAnalysis.Fixture{index}",
                    new
                    {
                        Name = $"Microsoft.CodeAnalysis.Fixture{index}",
                        Version = version,
                        Feed = "https://api.nuget.org/v3/index.json",
                    }))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            WriteRaw(
                $"roslyn-dotnet-{fileVersion}.json",
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [$"roslyn-dotnet-{fileVersion}"] = packages,
                }));
        }

        public void WriteRaw(string fileName, string contents) =>
            File.WriteAllText(Path.Combine(manifests, fileName), contents);

        public void Dispose()
        {
            if (Directory.Exists(Repository))
                Directory.Delete(Repository, recursive: true);
        }
    }
}
