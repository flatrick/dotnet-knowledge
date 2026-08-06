#!/usr/bin/env dotnet
#:property PublishAot=false
#:property Nullable=enable

// A throwaway driver, not an MCP server: it *is* the client. It starts probe-mcp-host.cs as a child
// with redirected stdio, speaks enough JSON-RPC to call `probe_progress` with a progress token, and
// prints every server->client message with the elapsed milliseconds at which it arrived.
//
// The question it answers: what does a compliant client see on the wire when the probe reports
// progress — how many notifications, in what order, and with what spacing? That is the reference
// the same call under a real MCP client is diffed against, which is how a client that drops
// progress is told from a server that never sent any.
//
// It cannot say anything about a real client's rendering, and it is not evidence about the shipped
// server: probe-mcp-host.cs shares none of DotNetKnowledge.Mcp's code. Use drive-sync-progress.cs
// for the real server's stages.
//
// Self-location: the checkout root is the grandparent of this script's own file, resolved via
// [CallerFilePath], so a worktree's copy drives *that worktree's* probe regardless of the shell's
// CWD. `--root` overrides it.
//
//   dotnet run --file scripts/probes/drive-probe-progress.cs
//   dotnet run --file scripts/probes/drive-probe-progress.cs -- --steps 6 --delay 100
//   dotnet run --file scripts/probes/drive-probe-progress.cs -- --root C:\path\to\checkout

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

var repoRoot = FindCheckoutRoot();
var steps = 4;
var delayMilliseconds = 300;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help":
            PrintUsage();
            return 0;
        case "--root" when i + 1 < args.Length:
            repoRoot = Path.GetFullPath(args[++i]);
            break;
        case "--steps" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSteps):
            steps = parsedSteps;
            i++;
            break;
        case "--delay" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedDelay):
            delayMilliseconds = parsedDelay;
            i++;
            break;
        default:
            Console.Error.WriteLine($"drive-probe-progress: unrecognized argument '{args[i]}'.");
            PrintUsage();
            return 2;
    }
}

var probePath = Path.Combine(repoRoot, "scripts", "probes", "probe-mcp-host.cs");
if (!File.Exists(probePath))
{
    Console.Error.WriteLine($"drive-probe-progress: no probe at '{probePath}'.");
    return 2;
}

Console.WriteLine($"probe: {probePath}");

var startInfo = new ProcessStartInfo
{
    FileName = "dotnet",
    WorkingDirectory = repoRoot,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
};
startInfo.ArgumentList.Add("run");
startInfo.ArgumentList.Add("--file");
startInfo.ArgumentList.Add(probePath);

using var child = new Process { StartInfo = startInfo };
child.Start();
_ = Task.Run(async () =>
{
    // The probe logs to stderr. Drain it so a full pipe cannot block the child.
    while (await child.StandardError.ReadLineAsync() is not null) { }
});

var stopwatch = Stopwatch.StartNew();

void Send(object message)
{
    var json = JsonSerializer.Serialize(message);
    child.StandardInput.WriteLine(json);
    child.StandardInput.Flush();
    Console.WriteLine($"--> {json}");
}

Send(new
{
    jsonrpc = "2.0",
    id = 1,
    method = "initialize",
    @params = new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "drive-probe-progress", version = "0.0.0" },
    },
});

// Wait for the initialize response before announcing initialized.
string? received;
while ((received = await child.StandardOutput.ReadLineAsync()) is not null)
{
    Console.WriteLine($"<-- [{stopwatch.ElapsedMilliseconds,5} ms] {received}");
    if (received.Contains("\"id\":1")) break;
}

Send(new { jsonrpc = "2.0", method = "notifications/initialized" });

Send(new
{
    jsonrpc = "2.0",
    id = 2,
    method = "tools/call",
    @params = new
    {
        name = "probe_progress",
        arguments = new { steps, delayMilliseconds },
        _meta = new { progressToken = "drive-token-1" },
    },
});

while ((received = await child.StandardOutput.ReadLineAsync()) is not null)
{
    Console.WriteLine($"<-- [{stopwatch.ElapsedMilliseconds,5} ms] {received}");
    if (received.Contains("\"id\":2")) break;
}

child.StandardInput.Close();
await child.WaitForExitAsync();
Console.WriteLine($"child exit={child.ExitCode}");
return 0;

static void PrintUsage()
{
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --file scripts/probes/drive-probe-progress.cs -- [options]");
    Console.WriteLine();
    Console.WriteLine("  --root <path>   Checkout whose probe-mcp-host.cs to drive (default: this script's checkout).");
    Console.WriteLine("  --steps <n>     Progress steps to request (default: 4).");
    Console.WriteLine("  --delay <ms>    Delay between steps (default: 300).");
    Console.WriteLine("  -h, --help      This text.");
}

static string FindCheckoutRoot([CallerFilePath] string scriptPath = "")
{
    var probesDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath))
        ?? throw new InvalidOperationException($"Could not determine directory of script path: {scriptPath}");
    var scriptsDir = Path.GetDirectoryName(probesDir)
        ?? throw new InvalidOperationException($"Could not determine scripts directory above: {probesDir}");
    return Path.GetDirectoryName(scriptsDir)
        ?? throw new InvalidOperationException($"Could not determine checkout root above: {scriptsDir}");
}
