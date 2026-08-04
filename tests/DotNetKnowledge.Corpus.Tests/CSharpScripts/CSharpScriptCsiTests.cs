using System.Reflection;
using DotNetKnowledge.CSharpScriptHost;
using DotNetKnowledge.Corpus.Tests.Execution;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Integration")]
public sealed class CSharpScriptCsiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DynamicData(nameof(CsiScenarios), DynamicDataDisplayName = nameof(CsiScenarioDisplayName))]
    public async Task CsiHostMatchesTheScenarioExpectation(string descriptorPath)
    {
        var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);
        var expected = descriptor.Expectations[ScriptHostKind.Csi];
        if (!new CsiToolchain().TryResolve(AppContext.BaseDirectory, out var csiPath, out var reason))
        {
            Assert.Inconclusive(reason);
        }

        var scenarioDirectory = Path.GetDirectoryName(descriptorPath)!;
        var arguments = new List<string>
        {
            Path.GetFullPath(Path.Combine(scenarioDirectory, descriptor.Entry))
        };
        arguments.AddRange(descriptor.Arguments.Select(argument =>
            descriptor.SupportFiles.Contains(argument, StringComparer.Ordinal)
                ? Path.GetFullPath(Path.Combine(scenarioDirectory, argument))
                : argument));

        var result = await new ProcessRunner().RunAsync(
            csiPath,
            arguments,
            workingDirectory: CSharpScriptTestPaths.RepositoryRoot,
            cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(
            expected.ExitCode,
            result.ExitCode,
            $"Standard output:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"Standard error:{Environment.NewLine}{result.StandardError}");
        CollectionAssert.AreEqual(expected.StandardOutput.ToArray(), NormalizeLines(result.StandardOutput));
        CollectionAssert.AreEqual(expected.StandardError.ToArray(), NormalizeLines(result.StandardError));
    }

    public static IEnumerable<object[]> CsiScenarios()
    {
        var examplesDirectory = Path.Combine(CSharpScriptTestPaths.ShowcaseRoot, "examples");
        foreach (var descriptorPath in Directory.EnumerateFiles(
                     examplesDirectory,
                     "scenario.json",
                     SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);
            if (descriptor.Hosts.Contains(ScriptHostKind.Csi))
            {
                yield return [descriptorPath];
            }
        }
    }

    public static string CsiScenarioDisplayName(MethodInfo methodInfo, object[] data)
    {
        _ = methodInfo;
        return Path.GetFileName(Path.GetDirectoryName((string)data[0])!);
    }

    private static string[] NormalizeLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }
}
