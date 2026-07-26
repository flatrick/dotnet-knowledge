using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotNetKnowledge.Corpus.Tests.Execution;

internal sealed class ProcessRunner
{
    private readonly TimeSpan timeout;

    public ProcessRunner()
        : this(TimeSpan.FromMinutes(5))
    {
    }

    internal ProcessRunner(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Process timeout must be positive.");
        }

        this.timeout = timeout;
    }

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
        using var processJob = ProcessJob.Create(process);

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), standardOutputTask, standardErrorTask)
                .WaitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            processJob?.Kill();
            KillProcessTree(process);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw new TimeoutException($"Process timed out after {timeout.TotalMinutes:0} minutes: {executable}.");
        }
        catch (OperationCanceledException)
        {
            processJob?.Kill();
            KillProcessTree(process);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw new OperationCanceledException(cancellationToken);
        }

        return new ProcessResult(
            executable,
            arguments.ToArray(),
            resolvedWorkingDirectory,
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static void KillProcessTree(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private sealed class ProcessJob : IDisposable
    {
        private readonly IntPtr handle;

        private ProcessJob(IntPtr handle)
        {
            this.handle = handle;
        }

        public static ProcessJob? Create(Process process)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create a process job.");
            }

            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                _ = CloseHandle(handle);
                throw new InvalidOperationException("Could not assign the process to a process job.");
            }

            return new ProcessJob(handle);
        }

        public void Kill()
        {
            if (!TerminateJobObject(handle, 1))
            {
                throw new InvalidOperationException("Could not terminate the process job.");
            }
        }

        public void Dispose()
        {
            _ = CloseHandle(handle);
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr objectHandle);
    }
}
