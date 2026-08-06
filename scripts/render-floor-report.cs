#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Renders csharp-placement-sweep.cs's JSON as a readable report: the headline counts, every row
// that compiles below its own version, the rows where the two readings disagree, and the full grid.
//
// Given `--baseline`, the JSON from `verify-feature-floors.cs -- --json` is summarized alongside
// for contrast. That section reports the pre-existing floor verdicts and is never a result of the
// sweep; without the file the section is omitted rather than guessed at.
//
//   dotnet scripts/render-floor-report.cs -- --sweep .scratch/sweep.json
//   dotnet scripts/render-floor-report.cs -- --sweep .scratch/sweep.json --baseline .scratch/floors-cs.json

using System.Text;
using System.Text.Json;

string? sweepPath = null;
string? baselinePath = null;
string? outPath = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help":
            PrintUsage();
            return 0;
        case "--sweep" when i + 1 < args.Length:
            sweepPath = Path.GetFullPath(args[++i]);
            break;
        case "--baseline" when i + 1 < args.Length:
            baselinePath = Path.GetFullPath(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            outPath = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine($"render-floor-report: unrecognized argument '{args[i]}'.");
            PrintUsage();
            return 2;
    }
}

if (sweepPath is null)
{
    Console.Error.WriteLine("render-floor-report: --sweep is required.");
    Console.Error.WriteLine("Produce it first: dotnet scripts/csharp-placement-sweep.cs -- --out <path>");
    PrintUsage();
    return 2;
}

if (!File.Exists(sweepPath))
{
    Console.Error.WriteLine($"render-floor-report: no sweep JSON at '{sweepPath}'.");
    Console.Error.WriteLine("Produce it first: dotnet scripts/csharp-placement-sweep.cs -- --out <path>");
    return 2;
}

if (baselinePath is not null && !File.Exists(baselinePath))
{
    Console.Error.WriteLine($"render-floor-report: no baseline JSON at '{baselinePath}'.");
    Console.Error.WriteLine("Produce it first: dotnet scripts/verify-feature-floors.cs -- --json > <path>");
    return 2;
}

outPath ??= Path.ChangeExtension(sweepPath, null) + "-report.txt";

var sweep = JsonDocument.Parse(File.ReadAllText(sweepPath)).RootElement;
var baseline = baselinePath is null
    ? (JsonElement?)null
    : JsonDocument.Parse(File.ReadAllText(baselinePath)).RootElement;

string[] ladder = ["1.0", "1.2", "2.0", "3.0", "4.0", "5.0", "6.0", "7.0", "7.1", "7.2", "7.3", "8.0"];
int Idx(string? v) => v is null ? -1 : Array.IndexOf(ladder, v);

var rows = sweep.GetProperty("rows").EnumerateArray()
    .Select(r => new
    {
        Version = r.GetProperty("Version").GetString()!,
        Group = r.GetProperty("Group").GetString()!,
        HomePin = r.GetProperty("HomePin").GetString()!,
        TopPinOk = r.GetProperty("TopPinOk").GetBoolean(),
        Floor = r.GetProperty("PlacementFloor").GetString(),
        FixedFloor = r.GetProperty("FixedRefFloor").GetString(),
        Moves = r.GetProperty("MovesRungs").ValueKind == JsonValueKind.Null
            ? -1 : r.GetProperty("MovesRungs").GetInt32(),
        Contiguous = r.GetProperty("Contiguous").GetBoolean(),
        UnsafeScoped = r.GetProperty("UnsafeScoped").GetBoolean(),
        Cells = r.GetProperty("Cells").EnumerateArray()
            .Select(c => (Pin: c.GetProperty("Pin").GetString()!,
                          Ok: c.GetProperty("Ok").GetBoolean(),
                          Error: c.GetProperty("Error").GetString() ?? ""))
            .ToArray(),
    })
    .OrderBy(r => Idx(r.Version))
    .ThenBy(r => r.Group, StringComparer.Ordinal)
    .ToArray();

var w = new StringBuilder();
void W(string line = "") => w.AppendLine(line);
string Grid(IEnumerable<(string Pin, bool Ok, string Error)> cells) =>
    string.Join(" ", cells.Select(c => $"{c.Pin}={(c.Ok ? "OK" : "no")}"));

W("C# measured-floor sweep -- placement floors for every row in the net48 C# corpus");
W("==============================================================================");
W();
W("Method");
W("------");
W("For each distinct (feature version, group) row, the row's source files were compiled");
W("standalone with Visual Studio's csc at every project pin at or below the row's own version,");
W("against that pin project's MSBuild-resolved reference set and DefineConstants. This is");
W("verify-feature-floors.cs's compile method with one change: the descent walks the whole");
W("project ladder instead of stopping one rung down.");
W();
W("Two readings were taken at every cell:");
W("  placement floor  -- each pin compiled against THAT pin project's reference set.");
W("                      Answers \"could the project pinned here hold this row?\"");
W("  fixed-ref floor  -- each pin compiled against the row's HOME project reference set,");
W("                      varying only /langversion. This is what verify-feature-floors.cs");
W("                      already does, extended down the ladder.");
W();
W("Mainline pin ladder: 1.0 2.0 3.0 4.0 5.0 6.0 7.0 7.1 7.2 7.3 8.0");
W("Unsafe pin ladder:   1.0 8.0   (CSharp_v1.0-Unsafe, CSharp_v8.0-Unsafe; compiled with /unsafe+)");
W();
W($"Rows swept: {rows.Length}.  Compiles: {sweep.GetProperty("compiles").GetInt32()}.  "
  + $"Grid time: {sweep.GetProperty("elapsedSeconds").GetDouble():F1}s.");
W();
W("Coverage");
W("--------");
W($"  rows whose top-pin control compile failed: {rows.Count(r => !r.TopPinOk)}"
  + "   (0 = every row compiles in isolation somewhere)");
W($"  rows with a non-contiguous accept set:     {rows.Count(r => !r.Contiguous)}"
  + "   (0 = acceptance is monotone in the pin everywhere)");

var controlProjects = sweep.TryGetProperty("controlProjects", out var cp) ? cp.GetInt32() : -1;
var controlFailures = sweep.TryGetProperty("controlFailures", out var cf)
    ? cf.EnumerateArray().Select(x => x.GetString()!).ToArray()
    : null;
if (controlFailures is null)
{
    W("  reference-set control compiles that failed: not recorded in this sweep JSON");
}
else
{
    W($"  reference-set control compiles that failed: {controlFailures.Length}"
      + $"   (of {controlProjects} projects"
      + (controlFailures.Length == 0 ? ")" : $"; {string.Join(", ", controlFailures)})"));
}

W();

var reach = rows.Where(r => r.Moves > 0).ToArray();
var relocating = rows.Where(r => r.Floor is not null && Idx(r.Floor) < Idx(r.HomePin)).ToArray();
var disagree = rows.Where(r => r.Floor != r.FixedFloor).ToArray();

W("Headline numbers");
W("----------------");
W($"  distinct rows                                            {rows.Length}");
W($"  rows that compile below their own version                {reach.Length}");
W($"  rows that would actually RELOCATE under per-pin placement {relocating.Length}");
W($"  rows whose placement floor is their own version          {rows.Count(r => r.Moves == 0)}");
W($"  rows where placement floor != fixed-ref floor            {disagree.Length}");
W();

W("Rows that compile below their own version");
W("-----------------------------------------");
foreach (var r in reach)
{
    var relocates = Idx(r.Floor) < Idx(r.HomePin) ? "RELOCATES" : "already housed at its floor";
    W($"  C# {r.Version}/{r.Group}");
    W($"      home pin {r.HomePin}   placement floor {r.Floor}   "
      + $"reach below own version: {r.Moves} rung(s)   {relocates}");
    W($"      grid: {Grid(r.Cells)}");
    var fail = r.Cells.LastOrDefault(c => !c.Ok);
    if (fail.Pin is not null)
    {
        W($"      highest rejecting pin {fail.Pin}: {fail.Error}");
    }

    W();
}

W("Placement floor vs fixed-reference floor");
W("----------------------------------------");
if (disagree.Length == 0)
{
    W("  no disagreements.");
}

foreach (var r in disagree)
{
    W($"  C# {r.Version}/{r.Group}: placement={r.Floor}  fixed-ref={r.FixedFloor}");
    var fail = r.Cells.LastOrDefault(c => !c.Ok);
    if (fail.Pin is not null)
    {
        W($"      per-pin rejection at {fail.Pin}: {fail.Error}");
    }
}

W();
W("Full grid (every row, ordered by feature version)");
W("------------------------------------------------");
W("  legend: <pin>=OK compiled, <pin>=no rejected");
W();
foreach (var r in rows)
{
    W($"  C# {r.Version,-4} {r.Group,-42} home={r.HomePin,-4} floor={r.Floor,-4} {Grid(r.Cells)}");
}

if (baseline is { } b)
{
    W();
    W("Pre-existing findings from verify-feature-floors.cs (NOT results of this sweep)");
    W("------------------------------------------------------------------------------");
    foreach (var group in b.GetProperty("results").EnumerateArray()
                 .GroupBy(x => x.GetProperty("Outcome").GetString()!)
                 .OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        W($"  {group.Key,-16} {group.Count()}");
    }

    W($"  SKIPPED          {string.Join("; ",
        b.GetProperty("skippedProjects").EnumerateArray().Select(x => x.GetString()))}");
    W($"  period compilers available: {string.Join(", ",
        b.GetProperty("periodCompilers").EnumerateArray().Select(x => x.GetString()))}");
    W();
    W("  verify-feature-floors.cs's INCONCLUSIVE placements include the distinct unsafe rows");
    W("  failing with CS0227, because it never passes /unsafe. This sweep passes /unsafe+, so");
    W("  those rows are settled in the grid above.");
}

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, w.ToString());
Console.WriteLine($"wrote {outPath}");
return 0;

static void PrintUsage()
{
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet scripts/render-floor-report.cs -- --sweep <path> [options]");
    Console.WriteLine();
    Console.WriteLine("  --sweep <path>      csharp-placement-sweep.cs's JSON. Required.");
    Console.WriteLine("  --baseline <path>   verify-feature-floors.cs --json output, summarized for contrast.");
    Console.WriteLine("  --out <path>        Report destination (default: <sweep>-report.txt).");
    Console.WriteLine("  -h, --help          This text.");
}
