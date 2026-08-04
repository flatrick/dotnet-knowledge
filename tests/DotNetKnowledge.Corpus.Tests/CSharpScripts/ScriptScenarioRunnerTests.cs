using System.Text.Json;
using DotNetKnowledge.CSharpScriptHost;
using Microsoft.CodeAnalysis.Scripting;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[DoNotParallelize]
[TestCategory("Unit")]
public sealed class ScriptScenarioRunnerTests
{
    private static readonly string[] CommandLineArgumentsOutput = ["alpha|two words"];
    private static readonly string[] ContinuedSubmissionOutput = ["5"];
    private static readonly string[] FileArgumentOutput = ["input payload"];
    private static readonly string[] LoadedValueOutput = ["loaded value: 42"];
    private static readonly string[] RootOutput = ["root"];
    private static readonly string[] TypedGlobalsOutput = ["agent: ready"];

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
    public async Task RunAsyncExposesDescriptorArgumentsThroughArgs()
    {
        var descriptorPath = CSharpScriptTestPaths.Descriptor("command-line-arguments");
        var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);

        var result = await new ScriptScenarioRunner().RunAsync(
            descriptor,
            Path.GetDirectoryName(descriptorPath)!,
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(CommandLineArgumentsOutput, result.StandardOutput.ToArray());
    }

    [TestMethod]
    public async Task RunAsyncExposesTheDescriptorPrefixThroughTypedGlobals()
    {
        var descriptorPath = CSharpScriptTestPaths.Descriptor("typed-globals");
        var descriptor = ScenarioDescriptorLoader.Load(descriptorPath);

        var result = await new ScriptScenarioRunner().RunAsync(
            descriptor,
            Path.GetDirectoryName(descriptorPath)!,
            TestContext.CancellationToken);

        CollectionAssert.AreEqual(TypedGlobalsOutput, result.StandardOutput.ToArray());
    }

    [TestMethod]
    public void TypedGlobalsDoesNotClaimCsiSupport()
    {
        var descriptor = ScenarioDescriptorLoader.Load(CSharpScriptTestPaths.Descriptor("typed-globals"));

        Assert.IsFalse(descriptor.Hosts.Contains(ScriptHostKind.Csi));
    }

    [TestMethod]
    public async Task RunAsyncCarriesStateAcrossOrderedSubmissions()
    {
        using var scenario = new TemporaryScenario(
            """
            var count = 2;
            int Add(int left, int right) => left + right;
            """,
            new Dictionary<string, string>
            {
                ["continue.csx"] =
                    """
                    count = Add(count, 3);
                    System.Console.WriteLine(count);
                    count
                    """
            },
            submissions: ["main.csx", "continue.csx"]);

        var result = await RunFromOutsideScenarioAsync(scenario);

        Assert.AreEqual("System.Int32", result.ReturnType);
        Assert.AreEqual("5", result.ReturnValue.GetRawText());
        CollectionAssert.AreEqual(ContinuedSubmissionOutput, result.StandardOutput.ToArray());
        Assert.AreEqual(2, result.CompletedSubmissionCount);
    }

    [TestMethod]
    public async Task RunAsyncCanonicalizesDeclaredSupportFileArgumentsOutsideTheScenarioDirectory()
    {
        using var scenario = new TemporaryScenario(
            "System.Console.WriteLine(System.IO.File.ReadAllText(Args[0]));",
            new Dictionary<string, string>
            {
                ["input.txt"] = "input payload"
            },
            arguments: ["input.txt"]);

        var result = await RunFromOutsideScenarioAsync(scenario);

        CollectionAssert.AreEqual(FileArgumentOutput, result.StandardOutput.ToArray());
    }

    [TestMethod]
    public async Task RunAsyncRejectsAContinuationBeforeExecutingItWhenCompilationFails()
    {
        using var scenario = new TemporaryScenario(
            "var initialized = true;",
            new Dictionary<string, string>
            {
                ["continue.csx"] =
                    """
                    System.IO.File.WriteAllText(Args[0], "executed");
                    MissingSymbol();
                    """
            },
            arguments: ["must-not-exist.txt"],
            submissions: ["main.csx", "continue.csx"]);

        var exception = await Assert.ThrowsExactlyAsync<CompilationErrorException>(() =>
            RunFromOutsideScenarioAsync(scenario));

        Assert.IsTrue(exception.Diagnostics.Any(diagnostic => diagnostic.Id == "CS0103"));
        Assert.IsFalse(File.Exists(Path.Combine(scenario.RootDirectory, "must-not-exist.txt")));
    }

    [TestMethod]
    [Timeout(3000)]
    public async Task RunAsyncRestoresConsoleWritersWhenAContinuationIsCanceled()
    {
        using var scenario = new TemporaryScenario(
            "var initialized = true;",
            new Dictionary<string, string>
            {
                ["continue.csx"] =
                    "await System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, CancellationToken);"
            },
            submissions: ["main.csx", "continue.csx"]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new ScriptScenarioRunner().RunAsync(
                scenario.Descriptor,
                scenario.DirectoryPath,
                cancellation.Token));

        Assert.AreSame(originalOutput, Console.Out);
        Assert.AreSame(originalError, Console.Error);
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

    [TestMethod]
    public async Task RunAsyncResolvesLoadRelativeToTheScenarioOutsideTheCurrentDirectory()
    {
        using var scenario = new TemporaryScenario(
            """
            #load "shared.csx"

            System.Console.WriteLine(Describe(42));
            """,
            new Dictionary<string, string>
            {
                ["shared.csx"] = "string Describe(int value) => $\"loaded value: {value}\";"
            });

        var result = await RunFromOutsideScenarioAsync(scenario);

        CollectionAssert.AreEqual(LoadedValueOutput, result.StandardOutput.ToArray());
    }

    [TestMethod]
    public async Task RunAsyncRejectsLoadOutsideTheScenarioEvenWhenTheFileExists()
    {
        using var scenario = new TemporaryScenario("#load \"../outside.csx\"");
        scenario.WriteOutsideFile("outside.csx", "System.Console.WriteLine(\"escaped\");");

        var exception = await Assert.ThrowsExactlyAsync<CompilationErrorException>(() =>
            RunFromOutsideScenarioAsync(scenario));

        Assert.IsTrue(
            exception.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "CS1504" &&
                diagnostic.GetMessage().Contains("outside.csx", StringComparison.Ordinal)),
            "Expected an unresolved-source diagnostic for outside.csx.");
    }

    [TestMethod]
    public async Task RunAsyncResolvesTheAllowlistedBclAssemblyReference()
    {
        using var scenario = new TemporaryScenario(
            """
            #r "System.Xml.Linq"

            using System.Xml.Linq;

            System.Console.WriteLine(XDocument.Parse("<root />").Root!.Name.LocalName);
            """);

        var result = await RunFromOutsideScenarioAsync(scenario);

        CollectionAssert.AreEqual(RootOutput, result.StandardOutput.ToArray());
    }

    [TestMethod]
    public async Task RunAsyncRejectsUnapprovedAssemblyReferenceEvenWhenTheFileExists()
    {
        using var scenario = new TemporaryScenario("#r \"unapproved.dll\"");
        File.Copy(
            typeof(System.Xml.Linq.XDocument).Assembly.Location,
            Path.Combine(scenario.DirectoryPath, "unapproved.dll"));

        var exception = await Assert.ThrowsExactlyAsync<CompilationErrorException>(() =>
            RunFromOutsideScenarioAsync(scenario));

        Assert.IsTrue(
            exception.Diagnostics.Any(diagnostic =>
                diagnostic.Id == "CS0006" &&
                diagnostic.GetMessage().Contains("unapproved.dll", StringComparison.Ordinal)),
            "Expected an unresolved-metadata diagnostic for unapproved.dll.");
    }

    private static async Task<ScriptSuccess> RunFromOutsideScenarioAsync(TemporaryScenario scenario)
    {
        var originalDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = scenario.RootDirectory;
        try
        {
            return await new ScriptScenarioRunner().RunAsync(
                scenario.Descriptor,
                scenario.DirectoryPath,
                CancellationToken.None);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private sealed class TemporaryScenario : IDisposable
    {
        public TemporaryScenario(
            string script,
            IReadOnlyDictionary<string, string>? supportFiles = null,
            IReadOnlyList<string>? arguments = null,
            IReadOnlyList<string>? submissions = null)
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-csx-{Guid.NewGuid():N}");
            DirectoryPath = Path.Combine(RootDirectory, "scenario");
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath, "main.csx"), script);
            foreach (var supportFile in supportFiles ?? new Dictionary<string, string>())
            {
                File.WriteAllText(Path.Combine(DirectoryPath, supportFile.Key), supportFile.Value);
            }

            var supportFileNames = JsonSerializer.Serialize(supportFiles?.Keys ?? []);
            var descriptorArguments = JsonSerializer.Serialize(arguments ?? []);
            var descriptorSubmissions = JsonSerializer.Serialize(submissions ?? []);
            File.WriteAllText(
                Path.Combine(DirectoryPath, "scenario.json"),
                $$"""
                {
                  "id": "temporary",
                  "entry": "main.csx",
                  "supportFiles": {{supportFileNames}},
                  "hosts": ["api"],
                  "arguments": {{descriptorArguments}},
                  "submissions": {{descriptorSubmissions}},
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

        public string RootDirectory { get; }

        public string DirectoryPath { get; }

        public ScenarioDescriptor Descriptor { get; }

        public void WriteOutsideFile(string relativePath, string contents) =>
            File.WriteAllText(Path.Combine(RootDirectory, relativePath), contents);

        public void Dispose() => Directory.Delete(RootDirectory, recursive: true);
    }
}
