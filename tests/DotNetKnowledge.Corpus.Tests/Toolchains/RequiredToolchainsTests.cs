using DotNetKnowledge.Corpus.Tests.Cases;

namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Integration")]
public sealed class RequiredToolchainsTests
{
    [TestMethod]
    public async Task EveryCheckedInCaseHasItsRequiredToolchains()
    {
        var cases = LoadCases();
        var inventory = await ToolchainInventory.Current;
        
        var missing = cases.SelectMany(testCase => testCase.Compilations)
            .Select(compilation => compilation.SdkBand)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ParseBand)
            .Select(sdkBand => MissingSdk(inventory, sdkBand))
            .OfType<string>()
            .ToList();

        missing.AddRange(from runtimeBand in cases.SelectMany(testCase => testCase.Runtimes)
                .Select(runtime => RuntimeBand(runtime.TargetFramework))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ParseBand)
            where !inventory.HasRuntime(runtimeBand)
            select $"Microsoft.NETCore.App runtime {runtimeBand}");

        if (missing.Count > 0)
        {
            Assert.Fail($"Missing required toolchains:{Environment.NewLine}{string.Join(Environment.NewLine, missing.Select(toolchain => $"- {toolchain}"))}");
        }
    }

    private static CorpusCase[] LoadCases()
    {
        var caseDirectory = Path.Combine(AppContext.BaseDirectory, "TestCases");
        if (!Directory.Exists(caseDirectory))
        {
            throw new InvalidOperationException($"Corpus case directory does not exist: {caseDirectory}.");
        }

        var casePaths = Directory.GetFiles(caseDirectory, "*.json", SearchOption.AllDirectories);
        return CorpusCaseLoader.LoadValidated(casePaths, RepositoryRoot())
            .Select(document => document.Case)
            .ToArray();
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

    private static string? MissingSdk(ToolchainInventory inventory, string sdkBand)
    {
        try
        {
            _ = inventory.ResolveSdk(sdkBand);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static string RuntimeBand(string targetFramework)
    {
        const string netPrefix = "net";
        if (!targetFramework.StartsWith(netPrefix, StringComparison.Ordinal) ||
            targetFramework.Length == netPrefix.Length)
        {
            throw new InvalidOperationException($"Runtime target framework must use the net<major>.<minor> format: {targetFramework}.");
        }

        return targetFramework[netPrefix.Length..];
    }

    private static Version ParseBand(string band) => Version.Parse($"{band}.0");
}
