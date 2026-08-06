#!/usr/bin/env dotnet
#:property PublishAot=false
#:property Nullable=enable

// A throwaway driver, not an MCP server: it *is* the client. It starts the real
// DotNetKnowledge.Mcp server as a child with redirected stdio, speaks enough JSON-RPC to call
// `sync_source` with a progress token, and prints every server->client message with the elapsed
// milliseconds at which it arrived.
//
// The question it answers: do the five sync stages actually reach the wire as
// notifications/progress, in stage order? A client that renders nothing cannot distinguish a server
// that sent nothing from a client that dropped it, and this driver is the only place the raw frames
// are visible.
//
// It cannot say what any *real* client does with those notifications. Use probe-mcp-host.cs's
// probe_progress for that; it runs inside the client.
//
// The cache is an isolated directory under the checkout's .scratch/, set through
// DOTNET_KNOWLEDGE_CACHE, so a run never touches the per-user source cache.
//
// Self-location: the checkout root is the grandparent of this script's own file, resolved via
// [CallerFilePath], so a worktree's copy drives *that worktree's* server regardless of the shell's
// CWD. `--root` overrides it.
//
//   dotnet run --file scripts/probes/drive-sync-progress.cs
//   dotnet run --file scripts/probes/drive-sync-progress.cs -- --source csharplang
//   dotnet run --file scripts/probes/drive-sync-progress.cs -- --root C:\path\to\checkout

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

var repoRoot = FindCheckoutRoot();
var source = "vblang";
string? cacheDir = null;

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
        case "--source" when i + 1 < args.Length:
            source = args[++i];
            break;
        case "--cache" when i + 1 < args.Length:
            cacheDir = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine($"drive-sync-progress: unrecognized argument '{args[i]}'.");
            PrintUsage();
            return 2;
    }
}

var serverDll = Path.Combine(
    repoRoot, "src", "DotNetKnowledge.Mcp", "bin", "Debug", "net10.0", "DotNetKnowledge.Mcp.dll");
if (!File.Exists(serverDll))
{
    Console.Error.WriteLine($"drive-sync-progress: no server build at '{serverDll}'.");
    Console.Error.WriteLine("Build it first: dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj");
    return 2;
}

var isolatedCache = cacheDir ?? Path.Combine(repoRoot, ".scratch", "sync-progress-cache");

Console.WriteLine($"server: {serverDll}");
Console.WriteLine($"cache:  {isolatedCache}");
Console.WriteLine($"source: {source}");

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
startInfo.ArgumentList.Add(serverDll);
startInfo.Environment["DOTNET_KNOWLEDGE_CACHE"] = isolatedCache;

using var child = new Process { StartInfo = startInfo };
child.Start();
_ = Task.Run(async () =>
{
    // The server logs to stderr. Drain it so a full pipe cannot block the child.
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
        clientInfo = new { name = "drive-sync-progress", version = "0.0.0" },
    },
});

string? received;
while ((received = await child.StandardOutput.ReadLineAsync()) is not null)
{
    Console.WriteLine($"<-- [{stopwatch.ElapsedMilliseconds,6} ms] {received}");
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
        name = "sync_source",
        arguments = new { name = source },
        _meta = new { progressToken = "sync-token-1" },
    },
});

while ((received = await child.StandardOutput.ReadLineAsync()) is not null)
{
    Console.WriteLine($"<-- [{stopwatch.ElapsedMilliseconds,6} ms] {received}");
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
    Console.WriteLine("  dotnet run --file scripts/probes/drive-sync-progress.cs -- [options]");
    Console.WriteLine();
    Console.WriteLine("  --root <path>     Checkout whose built server to drive (default: this script's checkout).");
    Console.WriteLine("  --source <name>   Source to sync (default: vblang).");
    Console.WriteLine("  --cache <path>    Isolated cache directory (default: <root>/.scratch/sync-progress-cache).");
    Console.WriteLine("  -h, --help        This text.");
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
