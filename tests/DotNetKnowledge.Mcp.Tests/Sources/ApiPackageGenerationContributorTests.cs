using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class ApiPackageGenerationContributorTests
{
    private static readonly string[] ExpectedPackageStages =
        ["package-download", "package-validate", "package-normalize"];
    private static readonly string[] ExpectedFrameworks = ["net10.0", "net472"];

    [TestMethod]
    public async Task BuildAsyncDownloadsValidatesAndNormalizesThePinnedPackage()
    {
        using var fixture = new PackageSyncFixture(["net472", "net10.0"]);
        var client = new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512);
        var contributor = new ApiPackageGenerationContributor(client, new PackageApiCorpusBuilder());
        var progress = new RecordingProgress();

        var states = await contributor.BuildAsync(
            fixture.Source,
            "pinned",
            fixture.Repository,
            fixture.Supplements,
            progress,
            CancellationToken.None);

        var state = states.Single();
        Assert.AreEqual("5.3.0", state.Version);
        Assert.AreEqual(fixture.ObservedSha512, state.Sha512);
        Assert.AreEqual("net10.0", state.DefaultFramework);
        CollectionAssert.AreEqual(ExpectedFrameworks, state.AvailableFrameworks.ToArray());
        Assert.IsFalse(Path.IsPathRooted(state.CorpusDirectory));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Supplements, state.CorpusDirectory, "net10.0.json")));
        var configuredPackage = fixture.Source.ApiPackages!.Single();
        Assert.AreEqual(configuredPackage.Sha512, client.SuppliedSha512);
        CollectionAssert.AreEqual(ExpectedPackageStages, progress.Values.ToArray());
        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Supplements, "*.nupkg", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public async Task BuildAsyncUsesTheObservedHeadCohortAndServerHash()
    {
        using var fixture = new PackageSyncFixture(["net10.0"], cohortVersion: "5.4.0");
        var client = new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512);
        var contributor = new ApiPackageGenerationContributor(client, new PackageApiCorpusBuilder());

        var state = (await contributor.BuildAsync(
            fixture.Source,
            "head:main",
            fixture.Repository,
            fixture.Supplements,
            progress: null,
            CancellationToken.None)).Single();

        Assert.AreEqual("5.4.0", client.Version);
        Assert.IsNull(client.SuppliedSha512);
        Assert.AreEqual("5.4.0", state.Version);
        Assert.AreEqual(fixture.ObservedSha512, state.Sha512);
        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Supplements, "*.nupkg", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public async Task BuildAsyncMatchesTheConfiguredDefaultFrameworkCaseInsensitively()
    {
        using var fixture = new PackageSyncFixture(["NET10.0"]);
        var contributor = new ApiPackageGenerationContributor(
            new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512),
            new PackageApiCorpusBuilder());

        var state = (await contributor.BuildAsync(
            fixture.Source,
            "pinned",
            fixture.Repository,
            fixture.Supplements,
            progress: null,
            CancellationToken.None)).Single();

        Assert.AreEqual("net10.0", state.DefaultFramework);
        Assert.AreEqual("NET10.0", state.AvailableFrameworks.Single());
    }

    [TestMethod]
    public async Task BuildAsyncRejectsAMissingDefaultAndDeletesTheArchive()
    {
        using var fixture = new PackageSyncFixture(["net472"]);
        var contributor = new ApiPackageGenerationContributor(
            new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512),
            new PackageApiCorpusBuilder());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => contributor.BuildAsync(
            fixture.Source,
            "pinned",
            fixture.Repository,
            fixture.Supplements,
            progress: null,
            CancellationToken.None));

        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Supplements, "*.nupkg", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public async Task BuildAsyncCancellationDeletesTheArchive()
    {
        using var fixture = new PackageSyncFixture(["net10.0"]);
        using var cancellation = new CancellationTokenSource();
        var client = new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512)
        {
            AfterCopy = cancellation.Cancel,
        };
        var contributor = new ApiPackageGenerationContributor(client, new PackageApiCorpusBuilder());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => contributor.BuildAsync(
            fixture.Source,
            "pinned",
            fixture.Repository,
            fixture.Supplements,
            progress: null,
            cancellation.Token));

        Assert.AreEqual(0, Directory.EnumerateFiles(fixture.Supplements, "*.nupkg", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public void AppliesToOnlyTheRoslynApiDocsRepositoryWithConfiguredPackages()
    {
        using var fixture = new PackageSyncFixture(["net10.0"]);
        var contributor = new ApiPackageGenerationContributor(
            new FixtureNuGetPackageClient(fixture.PackagePath, fixture.ObservedSha512),
            new PackageApiCorpusBuilder());

        Assert.IsTrue(contributor.AppliesTo(fixture.Source));
        Assert.IsFalse(contributor.AppliesTo(fixture.Source with { ApiPackages = [] }));
        Assert.IsFalse(contributor.AppliesTo(fixture.Source with { Repository = "example/other-api-docs" }));
    }

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Values { get; } = [];

        public void Report(string value) => Values.Add(value);
    }
}

internal sealed class FixtureNuGetPackageClient(string packagePath, string observedSha512) : INuGetPackageClient
{
    public Action? AfterCopy { get; init; }

    public Exception? Failure { get; set; }

    public string? Version { get; private set; }

    public string? SuppliedSha512 { get; private set; }

    public async Task<NuGetPackageDownload> DownloadAsync(
        ApiPackageDefinition package,
        string version,
        string? expectedSha512,
        string destination,
        CancellationToken cancellationToken)
    {
        Version = version;
        SuppliedSha512 = expectedSha512;
        if (Failure is not null)
            throw Failure;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var source = File.OpenRead(packagePath))
        await using (var target = File.Create(destination))
            await source.CopyToAsync(target, cancellationToken);

        AfterCopy?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return new NuGetPackageDownload(observedSha512, DateTimeOffset.Parse("2026-08-12T10:00:00Z"));
    }
}

internal sealed class PackageSyncFixture : IDisposable
{
    private static readonly string FixtureAssemblyPath = GetFixturePath("ApiFixtureAssemblyPath");

    public PackageSyncFixture(
        IReadOnlyList<string> frameworks,
        string cohortVersion = "5.3.0",
        bool validAssembly = true)
    {
        Root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-package-sync-{Guid.NewGuid():N}");
        Repository = Path.Combine(Root, "repository");
        Supplements = Path.Combine(Root, "supplements");
        Directory.CreateDirectory(Repository);
        Directory.CreateDirectory(Supplements);
        WriteManifest(Repository, cohortVersion);
        PackagePath = Path.Combine(Root, "fixture.nupkg");
        CreatePackage(PackagePath, frameworks, validAssembly);
        ObservedSha512 = Convert.ToBase64String(Enumerable.Repeat((byte)7, 64).ToArray());
        Source = new SourceDefinition(
            "dotnet/roslyn-api-docs",
            Repository,
            new string('1', 40),
            "main",
            ["dotnet/xml"],
            "Fixture Roslyn API source.",
            [Package(cohortVersion)]);
    }

    public string Root { get; }

    public string Repository { get; }

    public string Supplements { get; }

    public string PackagePath { get; }

    public string ObservedSha512 { get; }

    public SourceDefinition Source { get; }

    public static ApiPackageDefinition Package(string version, string defaultFramework = "net10.0") => new(
        "Microsoft.CodeAnalysis.Workspaces.MSBuild",
        "DotNetKnowledge.Mcp.Tests.ApiFixture",
        "https://api.nuget.org/v3/index.json",
        version,
        Convert.ToBase64String(Enumerable.Repeat((byte)3, 64).ToArray()),
        defaultFramework);

    public static void WriteManifest(string repository, string version)
    {
        var directory = Path.Combine(repository, "dotnet", "xml", "PackageInformation");
        Directory.CreateDirectory(directory);
        var manifest = new Dictionary<string, object>
        {
            [$"roslyn-dotnet-{version}"] = new Dictionary<string, object>
            {
                ["Microsoft.CodeAnalysis"] = new
                {
                    Name = "Microsoft.CodeAnalysis.Common",
                    Version = version,
                    Feed = "https://api.nuget.org/v3/index.json",
                },
            },
        };
        File.WriteAllText(
            Path.Combine(directory, $"roslyn-dotnet-{version}.json"),
            JsonSerializer.Serialize(manifest));
    }

    public static void CreatePackage(string path, IReadOnlyList<string> frameworks, bool validAssembly = true)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var framework in frameworks)
        {
            using (var assembly = archive.CreateEntry(
                $"lib/{framework}/DotNetKnowledge.Mcp.Tests.ApiFixture.dll").Open())
            {
                var source = validAssembly
                    ? File.ReadAllBytes(FixtureAssemblyPath)
                    : Encoding.UTF8.GetBytes("not managed metadata");
                assembly.Write(source);
            }

            using var xml = new StreamWriter(
                archive.CreateEntry($"lib/{framework}/DotNetKnowledge.Mcp.Tests.ApiFixture.xml").Open(),
                Encoding.UTF8,
                leaveOpen: false);
            xml.Write("<doc><members></members></doc>");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private static string GetFixturePath(string key) => typeof(PackageSyncFixture).Assembly
        .GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
        .Cast<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value
        ?? throw new InvalidOperationException($"Missing test fixture metadata '{key}'.");
}
