using System.Diagnostics;

namespace DotNetKnowledge.Mcp.Sources;

internal sealed class GitCommandRunner
{
    public static async Task<string> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        GitCommandKind kind,
        CancellationToken cancellationToken,
        GitTimeouts? timeouts = null)
    {
        var ceiling = (timeouts ?? GitTimeouts.Default).For(kind);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                // Git blocks during process startup when it inherits a piped stdin handle from a
                // parent whose own streams are pipes — which is what an MCP client creates. Even
                // `git --version` hangs. Redirecting is what fixes it; the stream is closed below
                // so no future invocation can block on a handle that never reaches end of file.
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GCM_INTERACTIVE"] = "Never";

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("Could not start git. Install Git and ensure it is on PATH.", exception);
        }

        process.StandardInput.Close();

        using var expiry = new CancellationTokenSource(ceiling);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            // The caller's cancellation and an expired tier both surface here. Only the second is a
            // fault worth naming; the first is the caller getting what it asked for.
            if (expiry.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"git {string.Join(' ', arguments)} exceeded its {ceiling.TotalSeconds:0.##}s "
                        + $"{kind} timeout and was terminated.");
            }

            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited with {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}
