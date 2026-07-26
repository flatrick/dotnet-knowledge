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

    [TestMethod]
    public async Task RunAsyncTimesOutWhenAChildRetainsRedirectedOutputAfterItsParentExits()
    {
        var runner = new ProcessRunner(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            runner.RunAsync("cmd", ["/c", "start /b ping -n 10 127.0.0.1"]));
    }

    [TestMethod]
    public async Task RunAsyncKillsTheProcessWhenCancellationIsRequested()
    {
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            runner.RunAsync("cmd", ["/c", "ping -n 10 127.0.0.1"], cancellationToken: cancellation.Token));
    }
}
