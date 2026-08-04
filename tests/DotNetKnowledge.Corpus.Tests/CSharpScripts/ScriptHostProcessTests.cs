using System.Text.Json;
using DotNetKnowledge.Corpus.Tests.Execution;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Integration")]
public sealed class ScriptHostProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task HostWritesOneSuccessJsonObject()
    {
        var result = await RunHostAsync(CSharpScriptTestPaths.Descriptor("expression-result"));

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(1, result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.AreEqual("expression-result", document.RootElement.GetProperty("scenarioId").GetString());
        Assert.AreEqual("api", document.RootElement.GetProperty("host").GetString());
        Assert.AreEqual("System.Int32", document.RootElement.GetProperty("returnType").GetString());
        Assert.AreEqual("42", document.RootElement.GetProperty("returnValue").GetRawText());
    }

    [TestMethod]
    public async Task HostFailureDoesNotWriteSuccessJson()
    {
        using var scenario = new TemporaryScenario("throw new System.InvalidOperationException(\"boom\");");

        var result = await RunHostAsync(Path.Combine(scenario.DirectoryPath, "scenario.json"));

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        using var document = JsonDocument.Parse(result.StandardError);
        Assert.AreEqual("runtime", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("System.InvalidOperationException", document.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("boom", document.RootElement.GetProperty("message").GetString());
    }

    private async Task<ProcessResult> RunHostAsync(string descriptorPath)
    {
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnetPath))
        {
            dotnetPath = "dotnet";
        }

        var hostPath = Path.Combine(
            CSharpScriptTestPaths.ShowcaseRoot,
            "host",
            "bin",
            "Release",
            "net10.0",
            "host.dll");
        return await new ProcessRunner().RunAsync(
            dotnetPath,
            [hostPath, descriptorPath],
            CSharpScriptTestPaths.RepositoryRoot,
            cancellationToken: TestContext.CancellationToken);
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
                      "exitCode": 1,
                      "returnType": null,
                      "returnValue": null,
                      "standardOutput": [],
                      "standardError": [],
                      "completedSubmissionCount": 0
                    }
                  }
                }
                """);
        }

        public string DirectoryPath { get; }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
