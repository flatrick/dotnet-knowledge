using System.Reflection;
using DotNetKnowledge.Corpus.Tests.Execution;
using DotNetKnowledge.Corpus.Tests.Projects;

namespace DotNetKnowledge.Corpus.Tests;

[TestClass]
[DoNotParallelize]
[TestCategory("Integration")]
public sealed class CorpusProjectBuildTests
{
    [TestMethod]
    [DynamicData(
        nameof(ProjectCoordinates),
        DynamicDataDisplayName = nameof(ProjectCoordinateDisplayName))]
    public async Task RebuildHasZeroWarningsAndErrors(string projectPath, string coordinate)
    {
        var runner = new ProcessRunner();
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnetPath))
        {
            dotnetPath = "dotnet";
        }

        // Persistent compiler/MSBuild processes can outlive a completed build while retaining the
        // runner's redirected pipes. Disable shared compilation, the CLI server, and node reuse so
        // completion and captured output belong to this build only.
        IReadOnlyDictionary<string, string?> buildEnvironment = new Dictionary<string, string?>
        {
            ["UseSharedCompilation"] = "false",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };
        var result = await runner.RunAsync(
            dotnetPath,
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:minimal"],
            RepositoryRoot(),
            buildEnvironment,
            cancellationToken: TestContext.CancellationToken);
        var completeOutput =
            $"{coordinate}{Environment.NewLine}" +
            $"{result.StandardOutput}{Environment.NewLine}" +
            result.StandardError;

        Assert.AreEqual(0, result.ExitCode, completeOutput);
        Assert.IsTrue(ContainsExactSummaryLine(result.StandardOutput, "0 Warning(s)"), completeOutput);
        Assert.IsTrue(ContainsExactSummaryLine(result.StandardOutput, "0 Error(s)"), completeOutput);
    }

    public TestContext TestContext { get; set; }

    internal static bool ContainsExactSummaryLine(string output, string summary)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(summary);

        return output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Any(line => string.Equals(line.Trim(), summary, StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> ProjectCoordinates()
    {
        var repositoryRoot = RepositoryRoot();
        foreach (var project in CorpusProjectDiscovery.FindAllSdkStyleProjects(repositoryRoot))
        {
            var language = project.RepositoryRelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                ? "VB"
                : "C#";
            yield return
            [
                Path.GetFullPath(
                    project.RepositoryRelativePath.Replace('/', Path.DirectorySeparatorChar),
                    repositoryRoot),
                $"{project.TargetFramework}/{language} {project.LanguageVersion}"
            ];
        }
    }

    public static string ProjectCoordinateDisplayName(MethodInfo methodInfo, object[] data)
    {
        _ = methodInfo;
        return (string)data[1];
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

[TestClass]
[TestCategory("Unit")]
public sealed class CorpusProjectBuildSummaryTests
{
    [TestMethod]
    [DataRow("  0 Warning(s)\r\n0 Error(s)\r\n", "0 Warning(s)")]
    [DataRow("0 Warning(s)\n  0 Error(s)  \n", "0 Error(s)")]
    public void ContainsExactSummaryLineAcceptsTrimmedZeroSummaryLines(string output, string summary)
    {
        Assert.IsTrue(CorpusProjectBuildTests.ContainsExactSummaryLine(output, summary));
    }

    [TestMethod]
    [DataRow("10 Warning(s)\r\n0 Error(s)\r\n", "0 Warning(s)")]
    [DataRow("0 Warning(s)\n10 Error(s)\n", "0 Error(s)")]
    public void ContainsExactSummaryLineRejectsNonzeroSummaryLines(string output, string summary)
    {
        Assert.IsFalse(CorpusProjectBuildTests.ContainsExactSummaryLine(output, summary));
    }
}
