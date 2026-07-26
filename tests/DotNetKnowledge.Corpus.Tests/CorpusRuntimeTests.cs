using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotNetKnowledge.Corpus.Tests.Cases;
using DotNetKnowledge.Corpus.Tests.Execution;
using DotNetKnowledge.Corpus.Tests.Probes;
using DotNetKnowledge.Corpus.Tests.Toolchains;

namespace DotNetKnowledge.Corpus.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class CorpusRuntimeTests
{
    [TestMethod]
    public void NormalizeOutputCanonicalizesOneTerminalNewline()
    {
        Assert.AreEqual("line\n", NormalizeOutput("line"));
        Assert.AreEqual("line\n", NormalizeOutput("line\n"));
        Assert.AreEqual("line\n", NormalizeOutput("line\r\n"));
    }

    [TestMethod]
    public void NormalizeOutputPreservesAnExtraTerminalNewline()
    {
        var oneTerminalNewline = NormalizeOutput("line\n");
        var twoTerminalNewlines = NormalizeOutput("line\n\n");

        Assert.AreEqual("line\n\n", twoTerminalNewlines);
        Assert.AreNotEqual(oneTerminalNewline, twoTerminalNewlines);
    }

    [TestMethod]
    [SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The test name is part of the corpus matrix contract.")]
    [DynamicData(
        nameof(RuntimeCoordinates),
        DynamicDataDisplayName = nameof(RuntimeCoordinateDisplayName))]
    public async Task Runtime_output_matches_declared_expectation(
        string casePath,
        int runtimeIndex,
        string coordinate)
    {
        var testCase = CorpusCaseLoader.Load(casePath);
        var expectation = testCase.Runtimes[runtimeIndex];
        var inventory = await ToolchainInventory.DiscoverCurrent(new ProcessRunner());

        InstalledSdk sdk;
        try
        {
            sdk = inventory.ResolveSdk(expectation.SdkBand);
            RequireRuntime(inventory, expectation.TargetFramework);
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail($"{coordinate}{Environment.NewLine}{exception.Message}");
            throw;
        }

        var compilation = new CompilationExpectation(
            expectation.SdkBand,
            expectation.TargetFramework,
            expectation.LanguageVersion,
            BuildOutcome.Success,
            []);
        using var build = await ProbeProject.BuildAsync(
            sdk,
            compilation,
            testCase.Source,
            expectation.Harness,
            TestContext.CancellationToken);
        var buildFailure = $"{coordinate}{Environment.NewLine}{build.CompleteOutput}";
        Assert.AreEqual(0, build.Process.ExitCode, buildFailure);
        Assert.IsFalse(
            build.CompleteOutput.Contains("warning ", StringComparison.OrdinalIgnoreCase),
            buildFailure);
        Assert.IsFalse(
            build.CompleteOutput.Contains("error ", StringComparison.OrdinalIgnoreCase),
            buildFailure);

        var result = await ProbeProject.RunAsync(
            sdk,
            build,
            TestContext.CancellationToken);
        var runtimeFailure =
            $"{coordinate}{Environment.NewLine}" +
            $"Standard output:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"Standard error:{Environment.NewLine}{result.StandardError}";
        Assert.AreEqual(expectation.ExitCode, result.ExitCode, runtimeFailure);
        Assert.AreEqual(0, result.ExitCode, runtimeFailure);
        Assert.AreEqual(string.Empty, result.StandardError, runtimeFailure);
        Assert.AreEqual(
            NormalizeOutput(string.Join('\n', expectation.StandardOutput)),
            NormalizeOutput(result.StandardOutput),
            runtimeFailure);
    }

    public TestContext TestContext { get; set; }

    public static IEnumerable<object[]> RuntimeCoordinates()
    {
        var inventory = ToolchainInventory
            .DiscoverCurrent(new ProcessRunner())
            .GetAwaiter()
            .GetResult();

        foreach (var casePath in CasePaths())
        {
            var testCase = CorpusCaseLoader.Load(casePath);
            for (var index = 0; index < testCase.Runtimes.Count; index++)
            {
                var expectation = testCase.Runtimes[index];
                yield return
                [
                    casePath,
                    index,
                    Coordinate(testCase, expectation, ResolveVersion(inventory, expectation.SdkBand))
                ];
            }
        }
    }

    public static string RuntimeCoordinateDisplayName(MethodInfo methodInfo, object[] data)
    {
        _ = methodInfo;
        return (string)data[2];
    }

    private static IEnumerable<string> CasePaths()
    {
        var caseDirectory = Path.Combine(AppContext.BaseDirectory, "TestCases");
        if (!Directory.Exists(caseDirectory))
        {
            throw new InvalidOperationException($"Corpus case directory does not exist: {caseDirectory}.");
        }

        return Directory.GetFiles(caseDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static void RequireRuntime(ToolchainInventory inventory, string targetFramework)
    {
        const string prefix = "net";
        if (!targetFramework.StartsWith(prefix, StringComparison.Ordinal) ||
            !inventory.HasRuntime(targetFramework[prefix.Length..]))
        {
            throw new InvalidOperationException(
                $"Required .NET runtime for target framework {targetFramework} is not installed.");
        }
    }

    private static string NormalizeOutput(string output)
    {
        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : normalized + '\n';
    }

    private static string ResolveVersion(ToolchainInventory inventory, string sdkBand)
    {
        try
        {
            return inventory.ResolveSdk(sdkBand).Version.ToString();
        }
        catch (InvalidOperationException)
        {
            return $"{sdkBand} (missing)";
        }
    }

    private static string Coordinate(
        CorpusCase testCase,
        RuntimeExpectation expectation,
        string sdkVersion) =>
        $"{testCase.Id} [SDK {sdkVersion}, {expectation.TargetFramework}, C# {expectation.LanguageVersion}]";
}
