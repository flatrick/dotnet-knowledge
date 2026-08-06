using System.Text.Json;
using DotNetKnowledge.Corpus.Tests.Cases;
using DotNetKnowledge.Corpus.Tests.Toolchains;

namespace DotNetKnowledge.Corpus.Tests.Probes;

[TestClass]
[TestCategory("Integration")]
public sealed class ProbeProjectTests
{
    private static InstalledSdk sdk10 = null!;

    [ClassInitialize]
    public static async Task ResolveSdk10FromConfiguredHost(TestContext testContext)
    {
        _ = testContext;
        var inventory = await ToolchainInventory.Current;
        sdk10 = inventory.ResolveSdk("10.0");
    }

    [TestMethod]
    public async Task BuildWritesExactSdkAndCompilationProperties()
    {
        var expectation = SuccessfulCompilation("net5.0", "10.0");
        var sourcePath = "tests/DotNetKnowledge.Corpus.Tests/Fixtures/FileScopedNamespace.cs";

        var result = await ProbeProject.BuildAsync(
            sdk10,
            expectation,
            sourcePath,
            harnessPath: null,
            TestContext.CancellationToken);
        var probeDirectory = result.ProjectDirectory;

        try
        {
            using var globalJson = JsonDocument.Parse(await File.ReadAllTextAsync(
                result.GlobalJsonPath,
                TestContext.CancellationToken));
            Assert.AreEqual("10.0.302", globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString());
            Assert.AreEqual("disable", globalJson.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());

            var project = await File.ReadAllTextAsync(result.ProjectPath, TestContext.CancellationToken);
            StringAssert.Contains(project, "<TargetFramework>net5.0</TargetFramework>");
            StringAssert.Contains(project, "<LangVersion>10.0</LangVersion>");
            StringAssert.Contains(project, "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>");
            StringAssert.Contains(project, "<MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>");
            StringAssert.Contains(project, "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
            StringAssert.Contains(project, "<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>");
            StringAssert.Contains(
                project,
                $"<Compile Include=\"{Path.GetFullPath(sourcePath, RepositoryRoot())}\" Link=\"Subject.cs\" />");
        }
        finally
        {
            result.Dispose();
        }

        Assert.IsFalse(Directory.Exists(probeDirectory), "Disposing a probe must remove its owned temporary directory.");
    }

    [TestMethod]
    public async Task AlwaysValidSourceBuildsWithSdk10Net10AndCSharp14()
    {
        using var result = await ProbeProject.BuildAsync(
            sdk10,
            SuccessfulCompilation("net10.0", "14.0"),
            "tests/DotNetKnowledge.Corpus.Tests/Fixtures/AlwaysValid.cs",
            harnessPath: null,
            TestContext.CancellationToken);

        AssertSuccessfulBuild(result);
    }

    [TestMethod]
    public async Task FileScopedNamespaceFailsWithCSharp9Diagnostic()
    {
        using var result = await ProbeProject.BuildAsync(
            sdk10,
            FailedCompilation("net5.0", "9.0", "CS8773"),
            "tests/DotNetKnowledge.Corpus.Tests/Fixtures/FileScopedNamespace.cs",
            harnessPath: null,
            TestContext.CancellationToken);

        Assert.AreNotEqual(0, result.Process.ExitCode, result.CompleteOutput);
        CollectionAssert.Contains(result.Diagnostics.ToArray(), "CS8773", result.CompleteOutput);
        Assert.HasCount(1, result.Diagnostics, result.CompleteOutput);
    }

    [TestMethod]
    public async Task FileScopedNamespaceBuildsWithCSharp10OnNet5()
    {
        using var result = await ProbeProject.BuildAsync(
            sdk10,
            SuccessfulCompilation("net5.0", "10.0"),
            "tests/DotNetKnowledge.Corpus.Tests/Fixtures/FileScopedNamespace.cs",
            harnessPath: null,
            TestContext.CancellationToken);

        AssertSuccessfulBuild(result);
    }

    [TestMethod]
    public void CleanupRejectsDirectoryOutsideOwnedRoot()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-cleanup-{Guid.NewGuid():N}");
        var ownedRoot = Path.Combine(testRoot, "owned");
        var outsideDirectory = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(ownedRoot);
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => ProbeResult.DeleteOwnedDirectory(ownedRoot, outsideDirectory));

            StringAssert.Contains(exception.Message, "outside the owned temporary root");
            Assert.IsTrue(Directory.Exists(outsideDirectory));
        }
        finally
        {
            Directory.Delete(outsideDirectory);
            Directory.Delete(ownedRoot);
            Directory.Delete(testRoot);
        }
    }

    public TestContext TestContext { get; set; }

    private static CompilationExpectation SuccessfulCompilation(string targetFramework, string languageVersion) =>
        new("10.0", targetFramework, languageVersion, BuildOutcome.Success, []);

    private static CompilationExpectation FailedCompilation(
        string targetFramework,
        string languageVersion,
        params string[] diagnostics) =>
        new("10.0", targetFramework, languageVersion, BuildOutcome.Failure, diagnostics);

    private static void AssertSuccessfulBuild(ProbeResult result)
    {
        Assert.AreEqual(0, result.Process.ExitCode, result.CompleteOutput);
        Assert.IsEmpty(result.Diagnostics, result.CompleteOutput);
        StringAssert.Contains(result.Process.StandardOutput, "0 Warning(s)", result.CompleteOutput);
        StringAssert.Contains(result.Process.StandardOutput, "0 Error(s)", result.CompleteOutput);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
