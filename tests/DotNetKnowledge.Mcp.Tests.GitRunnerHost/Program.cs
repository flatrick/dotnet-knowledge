using System.Diagnostics;
using DotNetKnowledge.Mcp.Sources;

// Calls git from a process that reproduces the fault condition: standard input is a pipe, and this
// process has an outstanding read on it. Both halves are required. A piped stdin alone does not
// hang git — measured at 34 ms — but a piped stdin the parent is concurrently reading hangs it
// indefinitely. An MCP stdio server always has a read pending on stdin, because that read is the
// transport, which is why the fault appears there and nowhere else.
//
// `runner` goes through the real GitCommandRunner. `inherit` deliberately bypasses it with a raw
// ProcessStartInfo, and exists to prove this harness still reproduces the fault the runner prevents.

var mode = args.Length > 0 ? args[0] : "runner";

// The transport-shaped read. Without it neither mode reproduces anything and `runner` passes for
// the wrong reason. Nothing ever writes to this pipe, so the read never completes — which is the
// point.
_ = Task.Run(() => Console.In.ReadToEndAsync());
await Task.Delay(300);

switch (mode)
{
    case "runner":
    {
        var output = await GitCommandRunner.RunAsync(
            Environment.CurrentDirectory,
            ["--version"],
            CancellationToken.None).ConfigureAwait(false);
        Console.Error.WriteLine(output.Trim());
        return 0;
    }

    case "inherit":
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    default:
        Console.Error.WriteLine($"unknown mode '{mode}'");
        return 2;
}
