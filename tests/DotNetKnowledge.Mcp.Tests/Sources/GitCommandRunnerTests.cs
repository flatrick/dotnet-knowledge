using System.Diagnostics;
using System.Reflection;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class GitCommandRunnerTests
{
    [TestMethod]
    public async Task RunnerCompletesWhenTheParentsStreamsArePipes()
    {
        var result = await RunFixtureAsync("runner", TimeSpan.FromSeconds(30));

        Assert.IsFalse(result.TimedOut, "The runner did not complete from a piped-stdio parent.");
        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.Stderr, "git version");
    }

    [TestMethod]
    public async Task InheritedStandardInputReproducesTheHang()
    {
        // The control. It bypasses the runner on purpose: if this ever completes, the harness has
        // stopped reproducing the fault and the test above no longer proves anything.
        var result = await RunFixtureAsync("inherit", TimeSpan.FromSeconds(10));

        Assert.IsTrue(
            result.TimedOut,
            "git completed with an inherited stdin handle. The harness no longer reproduces the "
                + "fault, so RunnerCompletesWhenTheParentsStreamsArePipes proves nothing.");
    }

    private static async Task<FixtureResult> RunFixtureAsync(string mode, TimeSpan timeout)
    {
        var fixturePath = typeof(GitCommandRunnerTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "GitRunnerHostPath")
            .Value!;
        Assert.IsTrue(File.Exists(fixturePath), $"Fixture not built: {fixturePath}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = Path.GetDirectoryName(fixturePath)!,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(fixturePath);
        process.StartInfo.ArgumentList.Add(mode);

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        using (var cancellation = new CancellationTokenSource(timeout))
        {
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }
        }

        var stderr = timedOut ? string.Empty : await stderrTask;
        if (timedOut && !process.HasExited)
            process.Kill(entireProcessTree: true);

        return new FixtureResult(timedOut, timedOut ? null : process.ExitCode, stderr);
    }

    [TestMethod]
    public async Task TimeoutNamesTheCommandThatExceededItsTier()
    {
        // A one-millisecond ceiling on a command that cannot finish before process start completes.
        // Deterministic, and it exercises the real timeout path rather than a simulated one.
        var timeouts = new GitTimeouts(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(15));

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            GitCommandRunner.RunAsync(
                Environment.CurrentDirectory,
                ["--version"],
                GitCommandKind.Quick,
                CancellationToken.None,
                timeouts));

        StringAssert.Contains(exception.Message, "git --version");
    }

    [TestMethod]
    public void WalkTierSitsBetweenQuickAndBulk()
    {
        var timeouts = GitTimeouts.Default;

        // A whole-tree read is neither a metadata command nor a transfer. Collapsing it onto either
        // neighbour is what put `git status` on a ten-second ceiling.
        Assert.IsTrue(
            timeouts.Quick < timeouts.Walk && timeouts.Walk < timeouts.Bulk,
            $"Expected Quick < Walk < Bulk, got {timeouts.Quick}, {timeouts.Walk}, {timeouts.Bulk}.");
        Assert.AreEqual(timeouts.Quick, timeouts.For(GitCommandKind.Quick));
        Assert.AreEqual(timeouts.Walk, timeouts.For(GitCommandKind.Walk));
        Assert.AreEqual(timeouts.Bulk, timeouts.For(GitCommandKind.Bulk));
    }

    [TestMethod]
    public async Task TimeoutNamesTheTierThatExpired()
    {
        var timeouts = new GitTimeouts(
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMinutes(15));

        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            GitCommandRunner.RunAsync(
                Environment.CurrentDirectory,
                ["--version"],
                GitCommandKind.Walk,
                CancellationToken.None,
                timeouts));

        StringAssert.Contains(exception.Message, "Walk timeout");
    }

    [TestMethod]
    public async Task CallerCancellationIsNotReportedAsATimeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // The caller getting what it asked for is not a fault. Asserting the negative rather than
        // an exact exception type keeps this robust: the cancellation can surface from the wait or
        // from either stream read, which differ in the derived type they throw.
        try
        {
            await GitCommandRunner.RunAsync(
                Environment.CurrentDirectory,
                ["--version"],
                GitCommandKind.Quick,
                cancellation.Token);
            Assert.Fail("Expected the cancelled token to abort the command.");
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }
        catch (TimeoutException)
        {
            Assert.Fail("Caller cancellation was misreported as a tier timeout.");
        }
    }

    private sealed record FixtureResult(bool TimedOut, int? ExitCode, string Stderr);
}
