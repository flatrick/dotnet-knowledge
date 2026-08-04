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

    [TestMethod]
    public async Task HostClassifiesMalformedDescriptorsAsValidationFailures()
    {
        using var descriptor = new TemporaryDescriptor("{");

        var result = await RunHostAsync(descriptor.Path);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        using var document = JsonDocument.Parse(result.StandardError);
        Assert.AreEqual("validation", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual(
            "DotNetKnowledge.CSharpScriptHost.ScenarioDescriptorValidationException",
            document.RootElement.GetProperty("type").GetString());
        StringAssert.Contains(document.RootElement.GetProperty("message").GetString()!, descriptor.Path);
    }

    [TestMethod]
    public async Task HostClassifiesSemanticDescriptorErrorsAsValidationFailures()
    {
        using var descriptor = new TemporaryDescriptor(
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

        var result = await RunHostAsync(descriptor.Path);

        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        using var document = JsonDocument.Parse(result.StandardError);
        Assert.AreEqual("validation", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual(
            "DotNetKnowledge.CSharpScriptHost.ScenarioDescriptorValidationException",
            document.RootElement.GetProperty("type").GetString());
        var message = document.RootElement.GetProperty("message").GetString()!;
        StringAssert.Contains(message, descriptor.Path);
        StringAssert.Contains(message, "Path does not exist: main.csx.");
    }

    [TestMethod]
    public async Task HostHelpDescribesTheCooperativeCancellationBoundary()
    {
        var result = await RunHostAsync("--help");

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.AreEqual(string.Empty, result.StandardError);
        StringAssert.Contains(result.StandardOutput, "30-second cooperative cancellation request");
        StringAssert.Contains(result.StandardOutput, "Ctrl+C");
        StringAssert.Contains(result.StandardOutput, "does not hard-stop");
        StringAssert.Contains(result.StandardOutput, "terminate the host process");
    }

    [TestMethod]
    [Timeout(45000)]
    public async Task HostReportsWhenACooperativeScriptObservesTheThirtySecondRequest()
    {
        using var scenario = new TemporaryScenario(
            "await System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, CancellationToken);");

        var result = await RunHostAsync(Path.Combine(scenario.DirectoryPath, "scenario.json"));

        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual(string.Empty, result.StandardOutput);
        using var document = JsonDocument.Parse(result.StandardError);
        Assert.AreEqual("timeout", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("System.TimeoutException", document.RootElement.GetProperty("type").GetString());
        var message = document.RootElement.GetProperty("message").GetString()!;
        StringAssert.Contains(message, "30-second cooperative cancellation request");
        StringAssert.Contains(message, "does not hard-stop");
        StringAssert.Contains(message, "terminate the host process");
    }

    private async Task<ProcessResult> RunHostAsync(string descriptorPath)
    {
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnetPath))
        {
            dotnetPath = "dotnet";
        }

        var hostPath = Path.Combine(AppContext.BaseDirectory, "host.dll");
        if (!File.Exists(hostPath))
        {
            throw new InvalidOperationException($"Script host does not exist in the active test output: {hostPath}.");
        }

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

    private sealed class TemporaryDescriptor : IDisposable
    {
        private readonly string directoryPath;

        public TemporaryDescriptor(string contents)
        {
            directoryPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dotnet-knowledge-csx-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            Path = System.IO.Path.Combine(directoryPath, "scenario.json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(directoryPath, recursive: true);
    }
}
