using System.Collections.Concurrent;
using System.Reflection;
using DotNetKnowledge.Corpus.Tests.Execution;
using DotNetKnowledge.Corpus.Tests.Projects;

namespace DotNetKnowledge.Corpus.Tests;

[TestClass]
[DoNotParallelize]
[TestCategory("Integration")]
public sealed class CorpusProjectBuildTests
{
    // Persistent compiler/MSBuild processes can outlive a completed build while retaining the
    // runner's redirected pipes. Disable shared compilation, the CLI server, and node reuse so
    // completion and captured output belong to this build only.
    private static readonly IReadOnlyDictionary<string, string?> BuildEnvironment =
        new Dictionary<string, string?>
        {
            ["UseSharedCompilation"] = "false",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1"
        };

    /// <summary>
    /// The project references every discovered project reaches. All eleven SDK-style C# libraries
    /// reference <c>CSharpComTypeLib</c> directly; the eleven net48 VB projects reach it through
    /// <c>CSharp_v8.0</c>, which their family's <c>Directory.Build.props</c> references for the
    /// ref-return row's subject. <c>-t:Rebuild</c> cleans and rebuilds a project's references too,
    /// so leaving these to the matrix would rebuild <c>CSharpComTypeLib</c>'s seven target
    /// frameworks once per matrix project and make two shared output directories the thing every
    /// concurrent build contends on. They are rebuilt once here instead, at the same
    /// zero-warning bar, and the matrix then builds with <c>--no-dependencies</c>.
    /// <para>
    /// The list is explicit because it cannot be read off the project files: the net48 VB reference
    /// is contributed by a props file through <c>$(MSBuildThisFileDirectory)</c>, which only an
    /// MSBuild evaluation resolves. A stale entry here is loud rather than silent — a matrix build
    /// whose reference was never built fails on the missing metadata file.
    /// </para>
    /// </summary>
    private static readonly string[] SharedProjectReferences =
    [
        "examples/language-features/CSharp/CSharpComTypeLib/CSharpComTypeLib.csproj",
        "examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj"
    ];

    private static readonly SemaphoreSlim BuildSlots = new(Environment.ProcessorCount);

    private static readonly ConcurrentDictionary<string, Task<ProcessResult>> MatrixBuilds =
        new(StringComparer.Ordinal);

    private static readonly Lazy<string[]> MatrixProjectPaths =
        new(() => [.. ProjectCoordinates().Select(coordinate => (string)coordinate[0])]);

    /// <summary>
    /// Rebuilds the shared project references, then starts every matrix build without awaiting it.
    /// The builds are independent processes over disjoint output directories, so the matrix is
    /// concurrency-bound rather than serial; each test then awaits its own project's result. The
    /// class stays <c>[DoNotParallelize]</c> because the concurrency lives here, not in the runner.
    /// </summary>
    [ClassInitialize]
    public static async Task StartProjectBuilds(TestContext testContext)
    {
        _ = testContext;

        foreach (var relativePath in SharedProjectReferences)
        {
            var projectPath = Path.GetFullPath(
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                RepositoryRoot());
            var result = await BuildAsync(projectPath, buildProjectReferences: true);
            AssertZeroWarningsAndErrors(result, relativePath);
        }

        foreach (var projectPath in MatrixProjectPaths.Value)
        {
            _ = MatrixBuilds.GetOrAdd(projectPath, StartMatrixBuild);
        }
    }

    [TestMethod]
    [DynamicData(
        nameof(ProjectCoordinates),
        DynamicDataDisplayName = nameof(ProjectCoordinateDisplayName))]
    public async Task RebuildHasZeroWarningsAndErrors(string projectPath, string coordinate)
    {
        var result = await MatrixBuilds.GetOrAdd(projectPath, StartMatrixBuild);

        AssertZeroWarningsAndErrors(result, coordinate);
    }

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

    private static void AssertZeroWarningsAndErrors(ProcessResult result, string subject)
    {
        var completeOutput =
            $"{subject}{Environment.NewLine}" +
            $"{result.StandardOutput}{Environment.NewLine}" +
            result.StandardError;

        Assert.AreEqual(0, result.ExitCode, completeOutput);
        Assert.IsTrue(ContainsExactSummaryLine(result.StandardOutput, "0 Warning(s)"), completeOutput);
        Assert.IsTrue(ContainsExactSummaryLine(result.StandardOutput, "0 Error(s)"), completeOutput);
    }

    private static Task<ProcessResult> StartMatrixBuild(string projectPath) =>
        Task.Run(async () =>
        {
            await BuildSlots.WaitAsync(CancellationToken.None);
            try
            {
                return await BuildAsync(projectPath, buildProjectReferences: false);
            }
            finally
            {
                _ = BuildSlots.Release();
            }
        });

    /// <summary>
    /// A build is started once and awaited by whichever test owns it, so it cannot carry a single
    /// test's cancellation token. <see cref="ProcessRunner"/>'s own timeout is what bounds it.
    /// </summary>
    /// <remarks>
    /// <c>-t:Rebuild</c> is the assertion's premise, not a precaution. An ordinary build over an
    /// unchanged tree logs <c>Skipping target "CoreCompile" because all output files are
    /// up-to-date</c> and still summarizes <c>0 Warning(s)</c> and <c>0 Error(s)</c> — a green gate
    /// that compiled nothing. Deleting each output directory first would restore the guarantee, but
    /// it costs what the rebuild costs, so it buys nothing.
    /// </remarks>
    private static async Task<ProcessResult> BuildAsync(string projectPath, bool buildProjectReferences)
    {
        List<string> arguments = ["build", projectPath, "-t:Rebuild", "--nologo", "-v:minimal"];
        if (!buildProjectReferences)
        {
            arguments.Add("--no-dependencies");
        }

        return await new ProcessRunner().RunAsync(
            DotnetPath(),
            arguments,
            RepositoryRoot(),
            BuildEnvironment,
            cancellationToken: CancellationToken.None);
    }

    private static string DotnetPath()
    {
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(dotnetPath) ? "dotnet" : dotnetPath;
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
