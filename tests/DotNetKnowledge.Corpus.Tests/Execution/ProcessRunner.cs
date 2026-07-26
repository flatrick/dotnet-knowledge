using System.Diagnostics;

namespace DotNetKnowledge.Corpus.Tests.Execution;

internal sealed class ProcessRunner
{
    private readonly TimeSpan timeout = TimeSpan.FromMinutes(5);

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedWorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = resolvedWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw new TimeoutException($"Process timed out after {timeout.TotalMinutes:0} minutes: {executable}.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        var output = await standardOutputTask;
        var error = await standardErrorTask;
        return new ProcessResult(
            executable,
            arguments.ToArray(),
            resolvedWorkingDirectory,
            process.ExitCode,
            output,
            error);
    }

    private static void KillProcessTree(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
