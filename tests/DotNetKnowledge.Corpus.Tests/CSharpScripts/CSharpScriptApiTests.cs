using System.Reflection;
using System.Text.Json;
using DotNetKnowledge.CSharpScriptHost;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[DoNotParallelize]
[TestCategory("Integration")]
public sealed class CSharpScriptApiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DynamicData(nameof(ApiScenarios), DynamicDataDisplayName = nameof(ApiScenarioDisplayName))]
    public async Task ApiHostMatchesTheScenarioExpectation(string descriptorPath)
    {
        var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);
        var expected = descriptor.Expectations[ScriptHostKind.Api];

        var result = await new ScriptScenarioRunner().RunAsync(
            descriptor,
            Path.GetDirectoryName(descriptorPath)!,
            TestContext.CancellationToken);

        Assert.AreEqual(0, expected.ExitCode);
        Assert.AreEqual(expected.ReturnType, result.ReturnType);
        if (expected.ReturnValue is { } returnValue)
        {
            Assert.AreEqual(returnValue.GetRawText(), result.ReturnValue.GetRawText());
        }
        else
        {
            Assert.AreEqual(JsonValueKind.Null, result.ReturnValue.ValueKind);
        }

        CollectionAssert.AreEqual(expected.StandardOutput.ToArray(), result.StandardOutput.ToArray());
        CollectionAssert.AreEqual(expected.StandardError.ToArray(), result.StandardError.ToArray());
        Assert.AreEqual(expected.CompletedSubmissionCount, result.CompletedSubmissionCount);
    }

    public static IEnumerable<object[]> ApiScenarios()
    {
        var examplesDirectory = Path.Combine(CSharpScriptTestPaths.ShowcaseRoot, "examples");
        foreach (var descriptorPath in Directory.EnumerateFiles(examplesDirectory, "scenario.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);
            if (descriptor.Hosts.Contains(ScriptHostKind.Api))
            {
                yield return [descriptorPath];
            }
        }
    }

    public static string ApiScenarioDisplayName(MethodInfo methodInfo, object[] data)
    {
        _ = methodInfo;
        return Path.GetFileName(Path.GetDirectoryName((string)data[0])!);
    }
}
