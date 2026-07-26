using System.Diagnostics;

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
    public async Task RunAsyncCanRemoveAnInheritedEnvironmentVariable()
    {
        var variableName = $"DOTNET_KNOWLEDGE_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variableName, "inherited");
        var runner = new ProcessRunner();

        try
        {
            var result = await runner.RunAsync(
                "cmd",
                ["/c", $"if defined {variableName} (exit /b 1) else (exit /b 0)"],
                environmentVariables: new Dictionary<string, string?> { [variableName] = null });

            Assert.AreEqual(0, result.ExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [TestMethod]
    public async Task RunAsyncTimesOutWhenAChildRetainsRedirectedOutputAfterItsParentExits()
    {
        var runner = new ProcessRunner(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            runner.RunAsync("cmd", ["/c", "start /b ping -n 10 127.0.0.1"]));
    }

    [TestMethod]
    [Timeout(3000)]
    public async Task RunAsyncKillsTheProcessWhenCancellationIsRequested()
    {
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            runner.RunAsync("cmd", ["/c", "ping -n 10 127.0.0.1"], cancellationToken: cancellation.Token));
    }

    [TestMethod]
    [Timeout(3000)]
    public async Task RunAsyncCancellationTerminatesDescendantThatRetainsRedirectedOutput()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-{Guid.NewGuid():N}.txt");
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var command = $"start \"\" /b cmd /c \"ping -n 2 127.0.0.1 > nul & echo completed > \"{marker}\"\"";

        try
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                runner.RunAsync("cmd", ["/c", command], cancellationToken: cancellation.Token));

            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            Assert.IsFalse(File.Exists(marker), "The descendant completed after cancellation instead of being terminated.");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [TestMethod]
    [Timeout(3000)]
    public async Task RunAsyncKillsStartedProcessWhenJobAssignmentFails()
    {
        var processId = 0;
        var runner = new ProcessRunner(
            TimeSpan.FromSeconds(1),
            process =>
            {
                processId = process.Id;
                throw new InvalidOperationException("Job assignment failed.");
            });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync("cmd", ["/c", "ping -n 10 127.0.0.1"]));

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(IsRunning(processId));
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
