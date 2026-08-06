#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Sweeps the whole C# project ladder: for every distinct row in the net48 C# corpus, compiles the
// row's source at every project pin at or below its own version and reports the lowest pin that
// still accepts it.
//
// verify-feature-floors.cs answers "is this row rejected ONE rung down", which is what a floor
// verdict needs. This tool answers the neighboring question — "what is the LOWEST project pin whose
// reference set and <LangVersion> compile this row" — by walking the whole ladder instead of
// stopping one rung down. It is the measurement behind MANIFEST.md's C# "Lowest accepted
// /langversion" column, and behind the decision not to restructure the C# tree into per-pin
// placement.
//
// It reuses verify-feature-floors.cs's compile method verbatim -- MSBuild resolves each project's
// reference set and DefineConstants, then csc compiles the row's files standalone under /noconfig
// with a response file -- and changes only the sweep.
//
// Two deliberate differences from verify-feature-floors.cs, both stated in the report:
//   1. Each candidate pin is compiled against THAT pin's project reference set, because the
//      question is whether that project could hold the row.
//   2. Rows housed in the *-Unsafe projects are compiled with /unsafe against the two-rung unsafe
//      ladder. verify-feature-floors.cs reports them INCONCLUSIVE (CS0227) because it never passes
//      /unsafe.
//
// Windows only; needs Visual Studio's MSBuild, same as the script it borrows from. Roughly three
// minutes. render-floor-report.cs turns the JSON into a readable report.
//
//   dotnet scripts/csharp-placement-sweep.cs                                  # JSON to stdout
//   dotnet scripts/csharp-placement-sweep.cs -- --out .scratch/sweep.json     # JSON to a file

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var ladder = new[]
{
    "1.0", "1.2", "2.0", "3.0", "4.0", "5.0", "6.0",
    "7.0", "7.1", "7.2", "7.3", "8.0",
};

string? outPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-h" or "--help":
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet scripts/csharp-placement-sweep.cs -- [options]");
            Console.WriteLine();
            Console.WriteLine("  --out <path>   Write the JSON here instead of stdout.");
            Console.WriteLine("  -h, --help     This text.");
            return 0;
        case "--out" when i + 1 < args.Length:
            outPath = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine($"sweep: unrecognized argument '{args[i]}'.");
            return 2;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
if (repoRoot is null)
{
    Console.Error.WriteLine("sweep: no repo root.");
    return 2;
}

var msbuild = FindMsBuild();
if (msbuild is null)
{
    Console.Error.WriteLine("sweep: no Visual Studio MSBuild.");
    return 2;
}

var csc = Path.Combine(Path.GetDirectoryName(msbuild)!, "Roslyn", "csc.exe");
if (!File.Exists(csc))
{
    Console.Error.WriteLine($"sweep: no csc.exe at '{csc}'.");
    return 2;
}

var corpusRoot = Path.Combine(
    repoRoot, "examples", "language-features", "CSharp", "dotNetFramework", "v4.8");
var workRoot = Path.Combine(repoRoot, ".artifacts", "feature-floors-placement");
Directory.CreateDirectory(workRoot);

// --- discover the projects and the rows each one holds -------------------------------------

var projects = new List<Proj>();
foreach (var dir in Directory.EnumerateDirectories(corpusRoot, "CSharp_v*")
             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
{
    var name = Path.GetFileName(dir);
    var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
    if (csproj is null)
    {
        Console.Error.WriteLine($"sweep: {name}: no .csproj");
        continue;
    }

    var pin = ReadLangVersion(csproj);
    if (pin is null)
    {
        Console.Error.WriteLine($"sweep: {name}: no <LangVersion>");
        continue;
    }

    var rows = new List<Row>();
    foreach (var versionDir in Directory.EnumerateDirectories(dir, "CSharp*"))
    {
        var version = FolderVersion(Path.GetFileName(versionDir), ladder);
        if (version is null)
        {
            continue;
        }

        foreach (var groupDir in Directory.EnumerateDirectories(versionDir)
                     .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var files = Directory
                .EnumerateFiles(groupDir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            rows.Add(new Row(version, Path.GetFileName(groupDir), files));
        }
    }

    if (rows.Count == 0)
    {
        Console.Error.WriteLine($"sweep: {name}: no CSharpN feature folders -- skipped");
        continue;
    }

    projects.Add(new Proj(name, csproj, pin, name.EndsWith("-Unsafe", StringComparison.Ordinal), rows));
}

var mainline = projects.Where(p => !p.Unsafe).OrderBy(p => Index(ladder, p.Pin)).ToArray();
var unsafeLadder = projects.Where(p => p.Unsafe).OrderBy(p => Index(ladder, p.Pin)).ToArray();

Console.Error.WriteLine($"sweep: mainline pins {string.Join(", ", mainline.Select(p => p.Pin))}");
Console.Error.WriteLine($"sweep: unsafe pins   {string.Join(", ", unsafeLadder.Select(p => p.Pin))}");

// --- resolve each project's inputs once -----------------------------------------------------

var inputs = new Dictionary<string, Inputs>(StringComparer.Ordinal);
foreach (var project in projects)
{
    var sw = Stopwatch.StartNew();
    var resolved = ResolveInputs(msbuild, project.CsprojPath);
    sw.Stop();
    Console.Error.WriteLine(
        $"sweep: {project.Name}: {resolved.References.Length} refs, defines='{resolved.Defines}' ({sw.Elapsed.TotalSeconds:F1}s)");
    inputs[project.Name] = resolved;
}

// A reference set that resolves nothing manufactures rejections. Control every pin before use, and
// carry the result into the JSON: a report that states the control passed must be reading a
// measurement, not a constant.
var controlFailures = new List<string>();
foreach (var project in projects)
{
    var control = Path.Combine(workRoot, "control.cs");
    File.WriteAllText(control, "internal class ReferenceSetControl { }\n");
    var result = Compile(csc, LangArg(project.Pin)!, [control], inputs[project.Name], workRoot, allowUnsafe: false);
    if (!result.Ok)
    {
        controlFailures.Add(project.Name);
        Console.Error.WriteLine($"sweep: CONTROL FAILED for {project.Name}: {result.Error}");
    }
}

// --- one canonical file set per distinct row ------------------------------------------------

var distinct = new Dictionary<string, Distinct>(StringComparer.Ordinal);
foreach (var project in projects)
{
    foreach (var row in project.Rows)
    {
        var key = row.Version + "|" + row.Group;
        if (!distinct.TryGetValue(key, out var entry))
        {
            entry = new Distinct(row.Version, row.Group, row.Files, project.Name, project.Unsafe, []);
            distinct[key] = entry;
        }
        else if (Index(ladder, project.Pin) < Index(ladder, FindPin(entry.SourceProject, projects)))
        {
            // Prefer the lowest-pinned copy on disk as the canonical text.
            distinct[key] = entry = entry with { Files = row.Files, SourceProject = project.Name };
        }

        entry.HeldBy.Add(project.Name);
    }
}

Console.Error.WriteLine($"sweep: {distinct.Count} distinct rows");

// --- the grid --------------------------------------------------------------------------------

var report = new List<RowReport>();
var compiles = 0;
var total = Stopwatch.StartNew();

foreach (var entry in distinct.Values
             .OrderBy(e => Index(ladder, e.Version))
             .ThenBy(e => e.Group, StringComparer.Ordinal))
{
    var scoped = entry.HeldBy.All(n => n.EndsWith("-Unsafe", StringComparison.Ordinal));
    var rungs = scoped ? unsafeLadder : mainline;

    // Sweep every pin at or below the row's own version -- plus, when the row's current home sits
    // higher than that (CSharp7_3 rows housed in CSharp_v8.0-Unsafe, CSharp7_1/AsyncMain first
    // held at CSharp_v7.2), the home pin itself, so the run always carries a control that proves
    // the row compiles in isolation somewhere.
    var home = rungs.First(p => entry.HeldBy.Contains(p.Name));
    var top = Math.Max(Index(ladder, entry.Version), Index(ladder, home.Pin));
    var candidates = rungs.Where(p => Index(ladder, p.Pin) <= top).ToArray();

    // Two readings of the same descent, differing only in the reference set.
    //   cells      -- each pin compiled against THAT pin's project reference set. Placement-derived:
    //                 "could the project pinned here hold this row?"
    //   fixedCells -- every pin compiled against the row's HOME project reference set, varying only
    //                 /langversion. Probe-derived: "how low does the compiler accept this source?"
    // Where the two floors agree, a per-pin restructure buys nothing a cheap /langversion descent
    // does not already give.
    var cells = new List<Cell>();
    var fixedCells = new List<Cell>();
    foreach (var candidate in candidates)
    {
        var arg = LangArg(candidate.Pin);
        if (arg is null)
        {
            cells.Add(new Cell(candidate.Pin, false, "no /langversion spelling"));
            fixedCells.Add(new Cell(candidate.Pin, false, "no /langversion spelling"));
            continue;
        }

        var result = Compile(csc, arg, entry.Files, inputs[candidate.Name], workRoot, scoped);
        compiles++;
        cells.Add(new Cell(candidate.Pin, result.Ok, result.Error));

        var fixedResult = Compile(csc, arg, entry.Files, inputs[home.Name], workRoot, scoped);
        compiles++;
        fixedCells.Add(new Cell(candidate.Pin, fixedResult.Ok, fixedResult.Error));
    }

    var accepted = cells.Where(c => c.Ok).Select(c => c.Pin).ToArray();
    var floor = accepted.Length == 0 ? null : accepted.OrderBy(p => Index(ladder, p)).First();
    var fixedAccepted = fixedCells.Where(c => c.Ok).Select(c => c.Pin).ToArray();
    var fixedFloor = fixedAccepted.Length == 0
        ? null
        : fixedAccepted.OrderBy(p => Index(ladder, p)).First();
    var topPin = cells.LastOrDefault()?.Pin;
    var topOk = cells.Count > 0 && cells[^1].Ok;

    // Contiguity check: is the accepted set a suffix of the candidate list? A hole means the row's
    // acceptance is not monotone in the pin, which would make "lowest pin that compiles it" a
    // misleading single number.
    var contiguous = true;
    var seenReject = false;
    for (var i = cells.Count - 1; i >= 0; i--)
    {
        if (!cells[i].Ok)
        {
            seenReject = true;
        }
        else if (seenReject)
        {
            contiguous = false;
        }
    }

    // Distance in project-ladder steps, not language-ladder steps: the question is how many
    // projects down the row would move, and the project ladder has no 1.2 rung.
    var distance = floor is null
        ? (int?)null
        : cells.Count - 1 - cells.FindIndex(c => c.Pin == floor);

    report.Add(new RowReport(
        entry.Version, entry.Group, entry.SourceProject, scoped,
        entry.HeldBy.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
        home.Pin, topPin, topOk, floor, fixedFloor, distance, contiguous, cells, fixedCells));

    Console.Error.WriteLine(
        $"  C# {entry.Version}/{entry.Group}: home={home.Pin} top={topPin} ok={topOk} floor={floor ?? "-"} fixedFloor={fixedFloor ?? "-"} moves={distance?.ToString() ?? "?"}");
}

total.Stop();

var json = JsonSerializer.Serialize(
    new
    {
        compiles,
        elapsedSeconds = total.Elapsed.TotalSeconds,
        controlProjects = projects.Count,
        controlFailures = controlFailures.ToArray(),
        rows = report,
    },
    new JsonSerializerOptions { WriteIndented = true });

if (outPath is null)
{
    Console.WriteLine(json);
}
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, json);
    Console.WriteLine($"wrote {outPath}");
}

Console.Error.WriteLine($"sweep: {compiles} compiles in {total.Elapsed}");
return 0;

// ---------------------------------------------------------------------------------------------

static string FindPin(string projectName, List<Proj> projects) =>
    projects.First(p => p.Name == projectName).Pin;

static int Index(string[] ladder, string version) => Array.IndexOf(ladder, version);

static string? LangArg(string version) => version switch
{
    "1.0" => "ISO-1",
    "1.2" => null,
    "2.0" => "ISO-2",
    _ => version,
};

static string? FolderVersion(string folderName, string[] ladder)
{
    if (!folderName.StartsWith("CSharp", StringComparison.Ordinal))
    {
        return null;
    }

    var suffix = folderName["CSharp".Length..];
    if (suffix.Length == 0)
    {
        return null;
    }

    var candidate = suffix.Replace('_', '.');
    if (!candidate.Contains('.'))
    {
        candidate += ".0";
    }

    return ladder.Contains(candidate) ? candidate : null;
}

static string? ReadLangVersion(string projectPath)
{
    foreach (var line in File.ReadLines(projectPath))
    {
        var open = line.IndexOf("<LangVersion>", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            continue;
        }

        var start = open + "<LangVersion>".Length;
        var close = line.IndexOf("</LangVersion>", start, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            continue;
        }

        var value = line[start..close].Trim();
        return value.Contains('.') ? value : value + ".0";
    }

    return null;
}

static Inputs ResolveInputs(string msbuild, string projectPath)
{
    Run(msbuild, $"\"{projectPath}\" -t:Restore -v:q -nologo");
    var json = Run(msbuild,
        $"\"{projectPath}\" -t:ResolveReferences -getItem:ReferencePath -getProperty:DefineConstants -nologo");

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var references = Array.Empty<string>();
        root.TryGetProperty("Items", out var items);
        if (items.ValueKind == JsonValueKind.Object && items.TryGetProperty("ReferencePath", out var paths))
        {
            references = paths.EnumerateArray()
                .Select(p => p.TryGetProperty("FullPath", out var full)
                    ? full.GetString()
                    : p.GetProperty("Identity").GetString())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();
        }

        string? defines = null;
        if (root.TryGetProperty("Properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("DefineConstants", out var define))
        {
            defines = define.GetString();
        }

        return new Inputs(references, defines);
    }
    catch (JsonException)
    {
        return new Inputs([], null);
    }
}

static CompileResult Compile(
    string csc, string langVersion, string[] files, Inputs inputs, string workRoot, bool allowUnsafe)
{
    var outDir = Path.Combine(workRoot, "probe");
    Directory.CreateDirectory(outDir);

    var rsp = new StringBuilder();
    rsp.AppendLine("/nologo");
    rsp.AppendLine("/nostdlib+");
    rsp.AppendLine("/target:library");
    rsp.AppendLine($"/langversion:{langVersion}");
    if (allowUnsafe)
    {
        rsp.AppendLine("/unsafe+");
    }
    if (!string.IsNullOrWhiteSpace(inputs.Defines))
    {
        rsp.AppendLine($"/define:{inputs.Defines}");
    }
    rsp.AppendLine($"/out:\"{Path.Combine(outDir, "probe.dll")}\"");
    foreach (var reference in inputs.References)
    {
        rsp.AppendLine($"/reference:\"{reference}\"");
    }
    foreach (var file in files)
    {
        rsp.AppendLine($"\"{file}\"");
    }

    var rspPath = Path.Combine(outDir, "probe.rsp");
    File.WriteAllText(rspPath, rsp.ToString());

    var psi = new ProcessStartInfo(csc, $"/noconfig @\"{rspPath}\"")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    var code = new Regex(@"\bCS\d{4}\b");
    var lines = (stdout + stderr).Split('\n').Select(l => l.Trim()).ToArray();
    var first = lines.FirstOrDefault(l => l.Contains(": error ", StringComparison.Ordinal))
        ?? lines.FirstOrDefault(l => code.IsMatch(l))
        ?? (process.ExitCode == 0 ? "" : "no diagnostic text");

    var match = code.Match(first);
    if (match.Success)
    {
        var marker = first.LastIndexOf(": ", match.Index, StringComparison.Ordinal);
        first = marker >= 0 ? first[(marker + 2)..] : first[match.Index..];
    }

    return new CompileResult(process.ExitCode == 0, first);
}

static string Run(string exe, string arguments)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return stdout;
}

static string? FindMsBuild()
{
    var vswhere = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswhere))
    {
        return null;
    }

    return Run(vswhere, "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe")
        .Split('\n')
        .Select(l => l.Trim())
        .FirstOrDefault(File.Exists);
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
            || File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

record Proj(string Name, string CsprojPath, string Pin, bool Unsafe, List<Row> Rows);
record Row(string Version, string Group, string[] Files);
record Distinct(
    string Version, string Group, string[] Files, string SourceProject, bool Unsafe, List<string> HeldBy);
record Inputs(string[] References, string? Defines);
record CompileResult(bool Ok, string Error);
record Cell(string Pin, bool Ok, string Error);
record RowReport(
    string Version,
    string Group,
    string SourceProject,
    bool UnsafeScoped,
    string[] HeldBy,
    string HomePin,
    string? TopPin,
    bool TopPinOk,
    string? PlacementFloor,
    string? FixedRefFloor,
    int? MovesRungs,
    bool Contiguous,
    List<Cell> Cells,
    List<Cell> FixedCells);
