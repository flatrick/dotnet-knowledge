using DotNetKnowledge.Corpus.Tests.Cases;
using DotNetKnowledge.Corpus.Tests.Execution;

namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Integration")]
public sealed class RequiredToolchainsTests
{
    [TestMethod]
    public async Task EveryCheckedInCaseHasItsRequiredToolchains()
    {
        var cases = LoadCases();
        var inventory = await ToolchainInventory.Discover("dotnet", new ProcessRunner());
        var missing = new List<string>();

        foreach (var sdkBand in cases
                     .SelectMany(testCase => testCase.Compilations)
                     .Select(compilation => compilation.SdkBand)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(ParseBand))
        {
            if (!TryResolveSdk(inventory, sdkBand))
            {
                missing.Add($".NET SDK {sdkBand}");
            }
        }

        foreach (var runtimeBand in cases
                     .SelectMany(testCase => testCase.Runtimes)
                     .Select(runtime => RuntimeBand(runtime.TargetFramework))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(ParseBand))
        {
            if (!inventory.HasRuntime(runtimeBand))
            {
                missing.Add($"Microsoft.NETCore.App runtime {runtimeBand}");
            }
        }

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

        return Directory.GetFiles(caseDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(CorpusCaseLoader.Load)
            .ToArray();
    }

    private static bool TryResolveSdk(ToolchainInventory inventory, string sdkBand)
    {
        try
        {
            _ = inventory.ResolveSdk(sdkBand);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
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
