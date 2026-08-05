using System.Diagnostics;
using System.Reflection;

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

    private sealed record FixtureResult(bool TimedOut, int? ExitCode, string Stderr);
}
