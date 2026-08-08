#!/usr/bin/env dotnet
#:property Nullable=enable

// Runs the FULL sync pipeline exactly as SourceSynchronizer.SyncCoreAsync does — clone,
// sparse-checkout, fetch, checkout, validate — with the same argv, environment, stream redirection
// and per-stage timeout tiers as GitCommandRunner. Every stage is timed independently.
//
// The question it answers: which stage is slow, and does any Quick-tier command (rev-parse, config,
// status) approach the 10 s ceiling on this machine? The corpus gotcha measured `git status` at
// 0.101 s warm on a fast disk with no anti-virus; that figure is what this probe re-measures.
//
//   dotnet run --file probe-sync.cs
//   dotnet run --file probe-sync.cs -- csharplang
//
// Unlike the server, this probe NEVER deletes the staging directory, so a failure leaves the
// downloaded data on disk for inspection. Delete the .probe-* directory yourself when done.

using System.Diagnostics;

var catalog = new Dictionary<string, (string Url, string Pin, string[] Sparse)>(StringComparer.Ordinal)
{
    ["csharplang"] = ("https://github.com/dotnet/csharplang.git",
        "36796924c898eb698d983b921001fa00cf689d9b",
        ["proposals", "spec", "meetings", "Language-Version-History.md"]),
    ["vblang"] = ("https://github.com/dotnet/vblang.git",
        "0712f0b8c91dfba593fc16f846c5821ed5cad2f9",
        ["proposals", "spec", "meetings", "Language-Version-History.md"]),
    ["roslyn-api-docs"] = ("https://github.com/dotnet/roslyn-api-docs.git",
        "4fed37999ba2fb00b90d4489358bb9a7763e49ef",
        ["dotnet/xml"]),
    ["dotnet-api-docs"] = ("https://github.com/dotnet/dotnet-api-docs.git",
        "a81557d32be7626cbd80b453b35b057cbcc472c7",
        ["xml"]),
    ["roslyn-wiki"] = ("https://github.com/dotnet/roslyn.git",
        "fca0128889ed81ab7a8754586be0ed18aadb4b14",
        ["docs/wiki"]),
    ["nuget-docs"] = ("https://github.com/NuGet/docs.microsoft.com-nuget.git",
        "2b52d770c577cf48b902dc176bdd3941a811d9d2",
        ["docs"]),
};

var quick = TimeSpan.FromSeconds(10);
var bulk = TimeSpan.FromMinutes(15);

var name = args.Length > 0 ? args[0] : "dotnet-api-docs";
if (!catalog.TryGetValue(name, out var definition))
{
    Console.Error.WriteLine($"Unknown source '{name}'. Known: {string.Join(", ", catalog.Keys)}");
    return 1;
}

// SourceCache.ResolveRoot, verbatim.
var configured = Environment.GetEnvironmentVariable("DOTNET_KNOWLEDGE_CACHE");
string root;
if (!string.IsNullOrWhiteSpace(configured))
{
    root = Path.GetFullPath(configured);
}
else
{
    var baseDirectory = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolderOption.Create);
    if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = Path.Combine(Path.GetTempPath(), "dotnet-knowledge-cache");
    root = Path.Combine(baseDirectory, "dotnet-knowledge", "sources");
}

Console.WriteLine("=== environment ===");
Console.WriteLine($"source     : {name}");
Console.WriteLine($"pin        : {definition.Pin}");
Console.WriteLine($"sparse     : {string.Join(", ", definition.Sparse)}");
Console.WriteLine($"cache root : {root}");
Console.WriteLine($"machine    : {Environment.MachineName}  cores {Environment.ProcessorCount}");
await ReportAsync(["--version"], quick);

Console.WriteLine();
Console.WriteLine("=== existing cache state ===");
if (Directory.Exists(root))
{
    foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        Console.WriteLine($"  {Path.GetFileName(entry)}");
    var stateDirectory = Path.Combine(root, ".state");
    if (Directory.Exists(stateDirectory))
        foreach (var file in Directory.EnumerateFiles(stateDirectory))
            Console.WriteLine($"  .state/{Path.GetFileName(file)}");
}
else
{
    Console.WriteLine("  <cache root does not exist>");
}

Directory.CreateDirectory(root);
var staging = Path.Combine(root, $".probe-{name}-{Guid.NewGuid():N}.tmp");
var timings = new List<(string Stage, string Tier, double Seconds, int ExitCode, bool TimedOut)>();

Console.WriteLine();
Console.WriteLine("=== pipeline (SourceSynchronizer.SyncCoreAsync) ===");

var ok = await StageAsync("clone", "Bulk", bulk,
    ["clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", "--", definition.Url, staging])
    && await StageAsync("sparse-checkout", "Bulk", bulk, ["sparse-checkout", "set", .. definition.Sparse], staging)
    && await StageAsync("fetch", "Bulk", bulk, ["fetch", "--depth", "1", "--quiet", "origin", definition.Pin], staging)
    && await StageAsync("checkout", "Bulk", bulk, ["checkout", "--detach", "--quiet", "FETCH_HEAD"], staging)
    && await StageAsync("validate/rev-parse", "Quick", quick, ["rev-parse", "HEAD"], staging)
    && await StageAsync("validate/config", "Quick", quick, ["config", "--get", "remote.origin.url"], staging)
    && await StageAsync("validate/status", "Quick", quick,
        ["status", "--porcelain", "--untracked-files=all"], staging);

Console.WriteLine();
Console.WriteLine("=== summary ===");
Console.WriteLine($"{"stage",-22} {"tier",-6} {"seconds",9}  {"ceiling",8}  result");
foreach (var (stage, tier, seconds, exitCode, timedOut) in timings)
{
    var ceiling = tier == "Quick" ? quick : bulk;
    var headroom = seconds / ceiling.TotalSeconds;
    var verdict = timedOut
        ? "*** EXCEEDED CEILING ***"
        : exitCode != 0
            ? $"git exit {exitCode}"
            : headroom > 0.5 ? $"ok  ({headroom:P0} of ceiling — TIGHT)" : "ok";
    Console.WriteLine($"{stage,-22} {tier,-6} {seconds,9:0.00}  {ceiling.TotalSeconds,7:0}s  {verdict}");
}

if (Directory.Exists(staging))
{
    var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList();
    Console.WriteLine();
    Console.WriteLine($"staging files : {files.Count:N0}");
    Console.WriteLine($"staging bytes : {files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024:N1} MB");
    Console.WriteLine($"staging path  : {staging}");
    Console.WriteLine("NOT deleted — the server would have deleted this on failure. Remove it yourself.");
}

Console.WriteLine();
Console.WriteLine(ok ? "PIPELINE SUCCEEDED" : "PIPELINE FAILED — see the stage marked above");
return ok ? 0 : 1;

async Task<bool> StageAsync(string stage, string tier, TimeSpan ceiling, string[] arguments, string? workingDirectory = null)
{
    var stopwatch = Stopwatch.StartNew();
    var outcome = await RunAsync(arguments, ceiling, workingDirectory);
    stopwatch.Stop();
    timings.Add((stage, tier, stopwatch.Elapsed.TotalSeconds, outcome.ExitCode, outcome.TimedOut));

    var status = outcome.TimedOut ? "TIMED OUT" : outcome.ExitCode == 0 ? "ok" : $"exit {outcome.ExitCode}";
    Console.WriteLine($"  {stage,-22} {stopwatch.Elapsed.TotalSeconds,8:0.00}s  {status}");

    if (outcome.TimedOut || outcome.ExitCode != 0)
    {
        Console.WriteLine($"    argv  : git {string.Join(' ', arguments)}");
        Console.WriteLine($"    stderr: {outcome.Stderr.Trim()}");
        return false;
    }

    // `status` must be empty for the server's integrity check to pass; show it when it is not.
    if (stage.EndsWith("status", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(outcome.Stdout))
    {
        var lines = outcome.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"    DIRTY : {lines.Length} entries — the server would reject this checkout");
        foreach (var line in lines.Take(5))
            Console.WriteLine($"            {line.TrimEnd()}");
    }

    return true;
}

async Task ReportAsync(string[] arguments, TimeSpan ceiling)
{
    var result = await RunAsync(arguments, ceiling);
    Console.WriteLine($"git        : {(result.Stdout + result.Stderr).Trim()}");
}

// GitCommandRunner.RunAsync, minus the exception mapping.
async Task<Outcome> RunAsync(IReadOnlyList<string> arguments, TimeSpan ceiling, string? workingDirectory = null)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };

    foreach (var argument in arguments)
        process.StartInfo.ArgumentList.Add(argument);

    process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
    process.StartInfo.Environment["GCM_INTERACTIVE"] = "Never";

    process.Start();
    process.StandardInput.Close();

    using var expiry = new CancellationTokenSource(ceiling);
    var stdoutTask = process.StandardOutput.ReadToEndAsync(expiry.Token);
    var stderrTask = process.StandardError.ReadToEndAsync(expiry.Token);

    try
    {
        await process.WaitForExitAsync(expiry.Token);
        return new Outcome(process.ExitCode, await stdoutTask, await stderrTask, TimedOut: false);
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        return new Outcome(-1, string.Empty, "<killed at ceiling>", TimedOut: true);
    }
}

readonly record struct Outcome(int ExitCode, string Stdout, string Stderr, bool TimedOut);
