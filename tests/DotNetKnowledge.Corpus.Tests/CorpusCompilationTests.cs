using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotNetKnowledge.Corpus.Tests.Cases;
using DotNetKnowledge.Corpus.Tests.Probes;
using DotNetKnowledge.Corpus.Tests.Toolchains;

namespace DotNetKnowledge.Corpus.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class CorpusCompilationTests
{
    [TestMethod]
    [SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The test name is part of the corpus matrix contract.")]
    [DynamicData(
        nameof(CompilationCoordinates),
        DynamicDataDisplayName = nameof(CompilationCoordinateDisplayName))]
    public async Task Build_matches_declared_expectation(
        string casePath,
        int compilationIndex,
        string coordinate)
    {
        var testCase = CorpusCaseLoader.Load(casePath);
        var expectation = testCase.Compilations[compilationIndex];
        var inventory = await ToolchainInventory.Current;

        InstalledSdk sdk;
        try
        {
            sdk = inventory.ResolveSdk(expectation.SdkBand);
        }
        catch (InvalidOperationException exception)
        {
            Assert.Inconclusive($"{coordinate}{Environment.NewLine}{exception.Message}");
            throw;
        }

        using var result = await ProbeProject.BuildAsync(
            sdk,
            expectation,
            testCase.Source,
            harnessPath: null,
            testCase.ProjectReferences,
            TestContext.CancellationToken);
        var failure = $"{coordinate}{Environment.NewLine}{result.CompleteOutput}";

        if (expectation.Outcome == BuildOutcome.Success)
        {
            Assert.AreEqual(0, result.Process.ExitCode, failure);
            Assert.IsFalse(
                result.CompleteOutput.Contains("warning ", StringComparison.OrdinalIgnoreCase),
                failure);
            Assert.IsFalse(
                result.CompleteOutput.Contains("error ", StringComparison.OrdinalIgnoreCase),
                failure);
            return;
        }

        Assert.AreNotEqual(0, result.Process.ExitCode, failure);
        foreach (var diagnostic in expectation.Diagnostics)
        {
            CollectionAssert.Contains(result.Diagnostics.ToArray(), diagnostic, failure);
        }
    }

    public TestContext TestContext { get; set; }

    public static IEnumerable<object[]> CompilationCoordinates()
    {
        var inventory = ToolchainInventory.Current.GetAwaiter().GetResult();

        foreach (var document in CorpusCaseLoader.LoadValidated(CasePaths(), RepositoryRoot()))
        {
            var casePath = document.Path;
            var testCase = document.Case;
            for (var index = 0; index < testCase.Compilations.Count; index++)
            {
                var expectation = testCase.Compilations[index];
                yield return
                [
                    casePath,
                    index,
                    Coordinate(testCase, expectation, ResolveVersion(inventory, expectation.SdkBand))
                ];
            }
        }
    }

    public static string CompilationCoordinateDisplayName(MethodInfo methodInfo, object[] data)
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
        CompilationExpectation expectation,
        string sdkVersion) =>
        $"{testCase.Id} [SDK {sdkVersion}, {expectation.TargetFramework}, C# {expectation.LanguageVersion}]";
}
