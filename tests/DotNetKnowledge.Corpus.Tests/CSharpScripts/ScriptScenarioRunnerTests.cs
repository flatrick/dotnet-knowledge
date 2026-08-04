using DotNetKnowledge.CSharpScriptHost;
using Microsoft.CodeAnalysis.Scripting;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[DoNotParallelize]
[TestCategory("Unit")]
public sealed class ScriptScenarioRunnerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunAsyncCapturesTypedFinalExpression()
    {
        var descriptorPath = CSharpScriptTestPaths.Descriptor("expression-result");
        var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);

        var result = await new ScriptScenarioRunner().RunAsync(
            descriptor,
            Path.GetDirectoryName(descriptorPath)!,
            TestContext.CancellationToken);

        Assert.AreEqual("System.Int32", result.ReturnType);
        Assert.AreEqual("42", result.ReturnValue.GetRawText());
        CollectionAssert.AreEqual(Array.Empty<string>(), result.StandardOutput.ToArray());
    }

    [TestMethod]
    public async Task RunAsyncRejectsWarnings()
    {
        using var scenario = new TemporaryScenario("void Check() { int unused; } Check();");

        var exception = await Assert.ThrowsExactlyAsync<CompilationErrorException>(() =>
            new ScriptScenarioRunner().RunAsync(
                scenario.Descriptor,
                scenario.DirectoryPath,
                TestContext.CancellationToken));

        Assert.IsTrue(exception.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0168"));
    }

    [TestMethod]
    public async Task RunAsyncPreservesRuntimeExceptionTypeAndMessage()
    {
        using var scenario = new TemporaryScenario("throw new System.InvalidOperationException(\"boom\");");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new ScriptScenarioRunner().RunAsync(
                scenario.Descriptor,
                scenario.DirectoryPath,
                TestContext.CancellationToken));

        Assert.AreEqual("boom", exception.Message);
    }

    [TestMethod]
    [Timeout(3000)]
    public async Task RunAsyncHonorsTheGlobalsCancellationToken()
    {
        using var scenario = new TemporaryScenario(
            "await System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, CancellationToken);");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new ScriptScenarioRunner().RunAsync(
                scenario.Descriptor,
                scenario.DirectoryPath,
                cancellation.Token));
    }

    private sealed class TemporaryScenario : IDisposable
    {
        public TemporaryScenario(string script)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-csx-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath, "main.csx"), script);
            File.WriteAllText(
                Path.Combine(DirectoryPath, "scenario.json"),
                """
                {
                  "id": "temporary",
                  "entry": "main.csx",
                  "supportFiles": [],
                  "hosts": ["api"],
                  "arguments": [],
                  "submissions": [],
                  "expectations": {
                    "api": {
                      "exitCode": 0,
                      "returnType": null,
                      "returnValue": null,
                      "standardOutput": [],
                      "standardError": [],
                      "completedSubmissionCount": 1
                    }
                  }
                }
                """);
            Descriptor = ScenarioDescriptorLoader.Load(Path.Combine(DirectoryPath, "scenario.json"));
        }

        public string DirectoryPath { get; }

        public ScenarioDescriptor Descriptor { get; }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
