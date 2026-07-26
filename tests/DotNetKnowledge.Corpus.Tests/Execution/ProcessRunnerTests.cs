namespace DotNetKnowledge.Corpus.Tests.Execution;

[TestClass]
[TestCategory("Unit")]
public sealed class ProcessRunnerTests
{
    private static readonly string[] ListSdksArguments = ["--list-sdks"];

    [TestMethod]
    public async Task RunAsyncRetainsExecutableAndArgumentsInResult()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync("dotnet", ListSdksArguments);

        Assert.AreEqual("dotnet", result.Executable);
        CollectionAssert.AreEqual(ListSdksArguments, result.Arguments.ToArray());
        Assert.AreEqual(Environment.CurrentDirectory, result.WorkingDirectory);
        Assert.AreEqual(0, result.ExitCode);
    }
}
