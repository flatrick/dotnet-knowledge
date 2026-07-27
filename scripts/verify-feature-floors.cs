#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Verify that every language-feature example actually requires the language version it is filed
// under, and report the ones whose requirement <LangVersion> cannot enforce.
//
// Why this exists: `/langversion:N` on a modern compiler is not the C# N compiler. Roslyn gates a
// construct on language version only where its binder explicitly calls CheckFeatureAvailability.
// Syntax-driven features (out variables, tuples, patterns, local functions) all got that call, so
// a too-low <LangVersion> rejects them. Semantic and attribute-driven features did not.
// Generalized async return types is the known case: `async ValueTask<T>`, and even a hand-rolled
// [AsyncMethodBuilder] type, compile clean under /langversion:5. Building the corpus at a pinned
// <LangVersion> therefore proves less than it appears to.
//
// So each feature group folder is probed twice, and escalated to a period compiler when the two
// probes disagree with expectations:
//
//   1. Compile the group standalone at its own language version. Must succeed, or the group needs
//      project context this script cannot reproduce and is reported INCONCLUSIVE.
//   2. Compile it at the language version one rung down the ladder.
//        rejected  -> GATED. <LangVersion> enforces the row. Nothing further to check.
//        accepted  -> suspicious. Escalate to step 3.
//   3. Compile it with a real compiler whose native ceiling is that lower version.
//        rejected  -> UNGATED. The example is genuine; Roslyn simply never gated the feature.
//        accepted  -> NOT-VERSION-SPECIFIC. The example does not demonstrate anything that
//                     requires its version. This is a defect in the example.
//      No period compiler for that boundary -> UNPROVEN, reported rather than assumed either way.
//
// A group filed in a project whose ceiling is below the group's own version is MISPLACED and is
// reported without probing.
//
// Period compilers, all used at their native ceiling rather than behind a /langversion flag:
//   C# 6 -- Microsoft.Net.Compilers 1.3.2, downloaded to .artifacts/ on first run
//   C# 5 -- the in-box compiler at %WINDIR%\Microsoft.NET\Framework64\v4.0.30319
//   C# 3 -- the in-box compiler at ...\v3.5
//   C# 2 -- the in-box compiler at ...\v2.0.50727
//
// There is no C# 4 compiler and no C# 1.x compiler. .NET 4.5 upgraded v4.0.30319's csc in place
// from C# 4 to C# 5, so the C# 4 binary is gone from any machine carrying .NET 4.5 or later, and
// .NET 1.0/1.1 do not install on a current Windows. Those two floors are reported UNPROVEN rather
// than guessed at. Older reference assemblies are not the missing piece -- the C# 2 and C# 3
// compilers read net48 reference assemblies without complaint, so no era-specific projects are
// needed to use them.
//
// Windows only, and needs Visual Studio's MSBuild: the corpus's net48 projects are non-SDK XML and
// resolve PackageReference assets only through the NuGet targets that ship with Visual Studio.
//
// Run from the repo root:
//   dotnet scripts/verify-feature-floors.cs
//   dotnet scripts/verify-feature-floors.cs -- --project CSharp_v7.0
//   dotnet scripts/verify-feature-floors.cs -- --json
//   dotnet scripts/verify-feature-floors.cs -- --offline
//
// Exit code is 1 when any group is MISPLACED or NOT-VERSION-SPECIFIC -- the two outcomes that mean
// the corpus is wrong. UNGATED, UNPROVEN and INCONCLUSIVE are findings about the toolchain's reach,
// not corpus defects, and do not fail the run.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var projectFilter = (string?)null;
var emitJson = false;
var offline = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            projectFilter = args[++i];
            break;
        case "--json":
            emitJson = true;
            break;
        case "--offline":
            offline = true;
            break;
        case "-h":
        case "--help":
            Console.WriteLine("Usage: dotnet scripts/verify-feature-floors.cs [-- --project <name>] [--json] [--offline]");
            return 0;
        default:
            Console.Error.WriteLine($"verify-feature-floors: unknown argument: {args[i]}");
            return 2;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
if (repoRoot is null)
{
    Console.Error.WriteLine("verify-feature-floors: could not locate the repo root.");
    return 2;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("verify-feature-floors: Windows only -- the corpus's net48 projects need Visual Studio's MSBuild.");
    return 2;
}

var msbuild = FindMsBuild();
if (msbuild is null)
{
    Console.Error.WriteLine("verify-feature-floors: could not locate Visual Studio's MSBuild.exe via vswhere.");
    return 2;
}

var modernCsc = Path.Combine(Path.GetDirectoryName(msbuild)!, "Roslyn", "csc.exe");
if (!File.Exists(modernCsc))
{
    Console.Error.WriteLine($"verify-feature-floors: no csc.exe beside MSBuild at '{modernCsc}'.");
    return 2;
}

// Period compilers, keyed by the language version each one natively tops out at.
var periodCompilers = new Dictionary<string, string>();

// The .NET Framework keeps its old compilers side by side, and each one's language ceiling is
// fixed. There is deliberately no v4.0 entry: .NET 4.5 upgraded the v4.0.30319 compiler in place
// from C# 4 to C# 5, so no C# 4 compiler survives on a modern machine. C# 4 is therefore the one
// floor in this range that cannot be settled by a native ceiling.
var frameworkRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64");

foreach (var (version, directory) in new[]
         {
             ("2.0", "v2.0.50727"),
             ("3.0", "v3.5"),
             ("5.0", "v4.0.30319"),
         })
{
    var candidate = Path.Combine(frameworkRoot, directory, "csc.exe");
    if (File.Exists(candidate))
    {
        periodCompilers[version] = candidate;
    }
}

var csharp6Csc = await AcquireCSharp6CompilerAsync(repoRoot, offline);
if (csharp6Csc is not null)
{
    periodCompilers["6.0"] = csharp6Csc;
}

// The C# probe's identity: how to find its files, spell its language versions on the compiler
// command line, and walk its version ladder. ProbeFloor and Below take this rather than reaching
// for Versions.Ladder or the free functions directly, so a second LanguageProfile can be threaded
// through the same code later.
var csharpProfile = new LanguageProfile(
    Name: "C#",
    SourceExtension: ".cs",
    ProjectExtension: ".csproj",
    Ladder: Versions.Ladder,
    FolderVersion: ParseFolderVersion,
    LangVersionArg: LangVersionArg,
    IsEnvironmentError: IsEnvironmentError);

var corpusRoot = Path.Combine(
    repoRoot, "examples", "language-features", "CSharp", "dotNetFramework", "v4.8");
if (!Directory.Exists(corpusRoot))
{
    Console.Error.WriteLine($"verify-feature-floors: corpus not found at '{corpusRoot}'.");
    return 2;
}

var projectDirs = Directory
    .EnumerateDirectories(corpusRoot, "CSharp_v*")
    .Where(d => projectFilter is null || Path.GetFileName(d) == projectFilter)
    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (projectDirs.Length == 0)
{
    Console.Error.WriteLine(projectFilter is null
        ? $"verify-feature-floors: no CSharp_v* projects under '{corpusRoot}'."
        : $"verify-feature-floors: no project named '{projectFilter}' under '{corpusRoot}'.");
    return 2;
}

var workRoot = Path.Combine(repoRoot, ".artifacts", "feature-floors");
Directory.CreateDirectory(workRoot);

// The same group folder is duplicated across the cumulative projects, and its floor is a property
// of the files rather than of the project holding them. Probe each distinct (version, content)
// once and reuse the verdict.
var floorCache = new Dictionary<string, Verdict>(StringComparer.Ordinal);
var results = new List<Result>();
var skippedProjects = new List<string>();

foreach (var projectDir in projectDirs)
{
    var projectName = Path.GetFileName(projectDir);
    var csproj = Directory.EnumerateFiles(projectDir, "*.csproj").FirstOrDefault();
    if (csproj is null)
    {
        skippedProjects.Add($"{projectName}: no .csproj");
        continue;
    }

    var ceiling = ReadLangVersion(csproj);
    if (ceiling is null)
    {
        skippedProjects.Add($"{projectName}: no <LangVersion> in {Path.GetFileName(csproj)}");
        continue;
    }

    var versionFolders = Directory
        .EnumerateDirectories(projectDir, "CSharp*")
        .Select(d => (Dir: d, Version: csharpProfile.FolderVersion(Path.GetFileName(d))))
        .Where(x => x.Version is not null)
        .OrderBy(x => LadderIndex(csharpProfile.Ladder, x.Version!))
        .ToArray();

    if (versionFolders.Length == 0)
    {
        skippedProjects.Add($"{projectName}: no CSharpN feature folders");
        continue;
    }

    Console.Error.WriteLine($"verify-feature-floors: {projectName} (ceiling {ceiling})");

    string[]? references = null;
    string? defines = null;

    foreach (var (versionDir, featureVersion) in versionFolders)
    {
        foreach (var groupDir in Directory.EnumerateDirectories(versionDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var group = Path.GetFileName(groupDir);
            var files = Directory.EnumerateFiles(groupDir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                results.Add(new Result(projectName, ceiling, featureVersion!, group,
                    "INCONCLUSIVE", "group folder holds no .cs files"));
                continue;
            }

            if (LadderIndex(csharpProfile.Ladder, featureVersion!) > LadderIndex(csharpProfile.Ladder, ceiling))
            {
                results.Add(new Result(projectName, ceiling, featureVersion!, group,
                    "MISPLACED", $"C# {featureVersion} group in a project pinned to {ceiling}"));
                continue;
            }

            var exemption = ExemptionReason(group);
            if (exemption is not null)
            {
                results.Add(new Result(projectName, ceiling, featureVersion!, group,
                    "EXEMPT", exemption));
                continue;
            }

            var key = featureVersion + "|" + HashFiles(files);
            if (!floorCache.TryGetValue(key, out var verdict))
            {
                // Resolving references costs a full MSBuild evaluation, so defer it until a
                // project actually has uncached work to do.
                if (references is null)
                {
                    (references, defines) = ResolveProjectInputs(msbuild, csproj);
                    if (references.Length == 0)
                    {
                        skippedProjects.Add($"{projectName}: MSBuild resolved no references");
                        break;
                    }
                }

                verdict = ProbeFloor(csharpProfile, featureVersion!, files, references, defines,
                    workRoot, modernCsc, periodCompilers);
                floorCache[key] = verdict;
            }

            results.Add(new Result(projectName, ceiling, featureVersion!, group,
                verdict.Outcome, verdict.Detail));
        }

        if (references is not null && references.Length == 0)
        {
            break;
        }
    }
}

Report(results, skippedProjects, periodCompilers, emitJson);

var failures = results.Count(r => r.Outcome is "MISPLACED" or "NOT-VERSION-SPECIFIC");
return failures > 0 ? 1 : 0;

// ---------------------------------------------------------------------------------------------

static Verdict ProbeFloor(
    LanguageProfile profile,
    string featureVersion,
    string[] files,
    string[] references,
    string? defines,
    string workRoot,
    string modernCsc,
    Dictionary<string, string> periodCompilers)
{
    // Step 1 -- the group must stand on its own at its own language version, or nothing below
    // this can be interpreted.
    var ownArg = profile.LangVersionArg(featureVersion);
    if (ownArg is null)
    {
        return new Verdict("UNPROVEN", $"C# {featureVersion} has no /langversion spelling");
    }

    var own = Compile(modernCsc, ownArg, files, references, defines, workRoot);
    if (!own.Succeeded)
    {
        return new Verdict("INCONCLUSIVE",
            $"does not compile standalone at /langversion:{ownArg} ({own.FirstError})");
    }

    var floor = Below(featureVersion, profile);
    if (floor is null)
    {
        return new Verdict("BASELINE",
            $"C# {featureVersion} is the oldest rung; there is no lower version to test against");
    }

    var floorArg = profile.LangVersionArg(floor);
    if (floorArg is null)
    {
        return new Verdict("UNPROVEN", $"C# {floor} has no /langversion spelling");
    }

    // Step 2 -- the modern compiler, held down to the rung below.
    var gated = Compile(modernCsc, floorArg, files, references, defines, workRoot);
    if (!gated.Succeeded)
    {
        return new Verdict("GATED", $"rejected at /langversion:{floorArg} ({gated.FirstError})");
    }

    // Step 3 -- a compiler whose native ceiling is the rung below. Only this tier can settle the
    // question in both directions: a compiler that predates the feature and still accepts the code
    // proves the code does not need the feature.
    if (periodCompilers.TryGetValue(floor, out var periodCsc))
    {
        // Environment control. A compiler this old may simply be unable to read the resolved
        // reference set, and that failure says nothing about the language.
        var control = Compile(periodCsc, langVersion: null, [TrivialSource(workRoot)], references,
            defines, workRoot);
        if (!control.Succeeded)
        {
            return new Verdict("INCONCLUSIVE",
                $"the C# {floor} compiler cannot read this project's reference set ({control.FirstError})");
        }

        var period = Compile(periodCsc, langVersion: null, files, references, defines, workRoot);
        if (period.Succeeded)
        {
            return new Verdict("NOT-VERSION-SPECIFIC",
                $"the C# {floor} compiler accepts it, so nothing here requires C# {featureVersion}");
        }

        return profile.IsEnvironmentError(period.FirstError)
            ? new Verdict("INCONCLUSIVE",
                $"the C# {floor} compiler could not process the group for a non-language reason ({period.FirstError})")
            : new Verdict("UNGATED",
                $"accepted at /langversion:{floorArg} but rejected by the C# {floor} compiler ({period.FirstError})");
    }

    // Step 4 -- no compiler tops out at this rung, so fall back to the pre-Roslyn compiler held to
    // that setting. This is a second implementation's gate rather than a native ceiling, so the
    // evidence is one-directional: a rejection proves the construct is version-dependent, but an
    // acceptance proves nothing, because a gate can be missing in both implementations for the
    // same reason. Acceptance therefore stays UNPROVEN and never accuses the example.
    var legacyArg = LegacyLangVersionArg(floor);
    var gate = periodCompilers
        .Where(kv => Versions.Ladder.IndexOf(kv.Key) > Versions.Ladder.IndexOf(floor))
        .OrderBy(kv => Versions.Ladder.IndexOf(kv.Key))
        .Select(kv => (Ceiling: kv.Key, Path: kv.Value))
        .FirstOrDefault();

    if (legacyArg is not null && gate.Path is not null)
    {
        // Control run first. Unless this compiler can handle the files at its own ceiling, a
        // failure at the floor says nothing about language versions.
        var control = Compile(gate.Path, langVersion: null, files, references, defines, workRoot);
        if (!control.Succeeded)
        {
            return new Verdict("INCONCLUSIVE",
                $"the C# {gate.Ceiling} compiler cannot process the group even at its own ceiling ({control.FirstError})");
        }

        var legacy = Compile(gate.Path, legacyArg, files, references, defines, workRoot);
        if (!legacy.Succeeded)
        {
            return new Verdict("UNGATED",
                $"accepted at /langversion:{floorArg} by the modern compiler but rejected by the C# {gate.Ceiling} compiler held to the same setting ({legacy.FirstError})");
        }

        return new Verdict("UNPROVEN",
            $"accepted at C# {floor} by the modern compiler and by the C# {gate.Ceiling} compiler held there, and no compiler topping out at C# {floor} exists to settle it");
    }

    return new Verdict("UNPROVEN",
        $"accepted at /langversion:{floorArg}, and no compiler for C# {floor} is available");
}

// A compile that exercises only the reference set, used to tell a metadata problem apart from a
// language rejection.
static string TrivialSource(string workRoot)
{
    var path = Path.Combine(workRoot, "control.cs");
    File.WriteAllText(path, "internal class ReferenceSetControl { }\n");
    return path;
}

static CompileResult Compile(
    string csc,
    string? langVersion,
    string[] files,
    string[] references,
    string? defines,
    string workRoot)
{
    var outDir = Path.Combine(workRoot, "probe");
    Directory.CreateDirectory(outDir);

    // /noconfig is deliberately absent here: csc honors it only on the command line. Left in the
    // response file it is ignored, csc.rsp gets read anyway, and its auto-references collide with
    // the resolved set as CS1703 on the older compilers.
    var rsp = new StringBuilder();
    rsp.AppendLine("/nologo");
    rsp.AppendLine("/nostdlib+");
    rsp.AppendLine("/target:library");
    if (langVersion is not null)
    {
        rsp.AppendLine($"/langversion:{langVersion}");
    }
    if (!string.IsNullOrWhiteSpace(defines))
    {
        rsp.AppendLine($"/define:{defines}");
    }
    rsp.AppendLine($"/out:\"{Path.Combine(outDir, "probe.dll")}\"");
    foreach (var reference in references)
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
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    // Prefer selecting by severity, not just by code: a warning line ahead of the actual error
    // must not be mistaken for it, or a GATED/UNGATED verdict ends up quoting the wrong
    // diagnostic. ": error " is tried first -- on an English toolchain this reproduces the
    // original literal match exactly, warnings excluded. Only when no line matches it at all (a
    // localized toolchain, where the severity word itself is translated) does this fall back to
    // matching any CSnnnn/BCnnnnn code, which can pick up a warning. That fallback trades severity
    // discrimination for not going silent -- the best available signal once the severity word
    // can't be matched by spelling, and strictly better than the old behavior of finding nothing.
    // CS codes are always 4 digits; BC codes run 4 or 5 (BC2001 vs. BC30002, BC36716, BC42024 --
    // all real codes this corpus has hit), so both need the wider count.
    var diagnosticCode = new Regex(@"\b(?:CS|BC)\d{4,5}\b");
    var output = stdout + stderr;
    var lines = output.Split('\n').Select(line => line.Trim()).ToArray();
    var firstError = lines.FirstOrDefault(line => line.Contains(": error ", StringComparison.Ordinal))
        ?? lines.FirstOrDefault(line => diagnosticCode.IsMatch(line))
        ?? (process.ExitCode == 0 ? "" : "no diagnostic text");

    // Keep only the severity word, code, and message; the absolute path in front is noise in a
    // report. The compiler's format is "<location>: <severity> <code>: <message>" -- the colon
    // and space right before the severity word are locale-independent even though the word itself
    // is not, so anchoring on the nearest ": " before the code keeps the severity word without
    // hardcoding its spelling.
    var match = diagnosticCode.Match(firstError);
    if (match.Success)
    {
        var marker = firstError.LastIndexOf(": ", match.Index, StringComparison.Ordinal);
        firstError = marker >= 0 ? firstError[(marker + 2)..] : firstError[match.Index..];
    }

    return new CompileResult(process.ExitCode == 0, firstError);
}

static (string[] References, string? Defines) ResolveProjectInputs(string msbuild, string csproj)
{
    Run(msbuild, $"\"{csproj}\" -t:Restore -v:q -nologo");

    var json = Run(msbuild,
        $"\"{csproj}\" -t:ResolveReferences -getItem:ReferencePath -getProperty:DefineConstants -nologo");

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var references = Array.Empty<string>();
        if (root.TryGetProperty("Items", out var items)
            && items.TryGetProperty("ReferencePath", out var paths))
        {
            // Deduplicate by simple name, not by path. MSBuild can resolve the same assembly
            // identity from two places (a reference assembly and a package), which the modern
            // compiler tolerates but older ones reject outright with CS1703.
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
            && properties.TryGetProperty("DefineConstants", out var define))
        {
            defines = define.GetString();
        }

        return (references, defines);
    }
    catch (JsonException)
    {
        return (Array.Empty<string>(), null);
    }
}

static string Run(string exe, string arguments)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return stdout;
}

static async Task<string?> AcquireCSharp6CompilerAsync(string repoRoot, bool offline)
{
    var dir = Path.Combine(repoRoot, ".artifacts", "period-compilers",
        $"microsoft.net.compilers.{Versions.CompilerPackage}");
    var csc = Path.Combine(dir, "tools", "csc.exe");
    if (File.Exists(csc))
    {
        return csc;
    }

    if (offline)
    {
        Console.Error.WriteLine(
            "verify-feature-floors: --offline and no cached C# 6 compiler; boundaries at C# 6 will report UNPROVEN.");
        return null;
    }

    var url = $"https://www.nuget.org/api/v2/package/Microsoft.Net.Compilers/{Versions.CompilerPackage}";
    Console.Error.WriteLine($"verify-feature-floors: downloading Microsoft.Net.Compilers {Versions.CompilerPackage}...");

    try
    {
        Directory.CreateDirectory(dir);
        var nupkg = Path.Combine(dir, "package.nupkg");

        using (var http = new HttpClient())
        await using (var stream = await http.GetStreamAsync(url))
        await using (var file = File.Create(nupkg))
        {
            await stream.CopyToAsync(file);
        }

        ZipFile.ExtractToDirectory(nupkg, dir, overwriteFiles: true);
        File.Delete(nupkg);
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
    {
        Console.Error.WriteLine(
            $"verify-feature-floors: could not fetch the C# 6 compiler ({ex.Message}); boundaries at C# 6 will report UNPROVEN.");
        return null;
    }

    return File.Exists(csc) ? csc : null;
}

static void Report(
    List<Result> results,
    List<string> skippedProjects,
    Dictionary<string, string> periodCompilers,
    bool emitJson)
{
    if (emitJson)
    {
        var payload = new
        {
            periodCompilers = periodCompilers.Keys.OrderBy(k => k).ToArray(),
            skippedProjects,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    // Every group is listed under its outcome. Nothing is capped -- a quietly shortened report
    // reads as a clean corpus.
    var order = new[]
    {
        "MISPLACED", "NOT-VERSION-SPECIFIC", "UNGATED", "UNPROVEN", "INCONCLUSIVE",
        "EXEMPT", "BASELINE", "GATED",
    };

    foreach (var outcome in order)
    {
        var rows = results.Where(r => r.Outcome == outcome).ToArray();
        if (rows.Length == 0)
        {
            continue;
        }

        Console.WriteLine();
        Console.WriteLine($"{outcome} ({rows.Length})");

        if (outcome == "EXEMPT")
        {
            // One line per distinct row; the reason is the point, not the project copies.
            foreach (var exemption in rows
                         .Select(r => (r.Group, r.Detail))
                         .Distinct()
                         .OrderBy(x => x.Group, StringComparer.Ordinal))
            {
                var copies = rows.Count(r => r.Group == exemption.Group);
                Console.WriteLine($"  {exemption.Group} ({copies} project copies)");
                Console.WriteLine($"      {exemption.Detail}");
            }

            continue;
        }

        if (outcome is "GATED" or "BASELINE")
        {
            // The healthy majority. Naming every project copy of every row would bury the rest.
            var groups = rows
                .Select(r => $"C# {r.FeatureVersion} {r.Group}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal)
                .ToArray();
            var note = outcome == "GATED"
                ? "<LangVersion> enforces these"
                : "C# 1.0 rows, with no lower version to test against";
            Console.WriteLine($"  {groups.Length} distinct groups across {rows.Length} project copies -- {note}.");
            continue;
        }

        foreach (var row in rows.OrderBy(r => r.Project, StringComparer.Ordinal)
                                .ThenBy(r => r.Group, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {row.Project} (ceiling {row.Ceiling})  C# {row.FeatureVersion}/{row.Group}");
            Console.WriteLine($"      {row.Detail}");
        }
    }

    if (skippedProjects.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"SKIPPED ({skippedProjects.Count})");
        foreach (var skipped in skippedProjects)
        {
            Console.WriteLine($"  {skipped}");
        }
    }

    var available = periodCompilers.Count == 0
        ? "none"
        : string.Join(", ", periodCompilers.Keys.OrderBy(k => k).Select(k => $"C# {k}"));

    Console.WriteLine();
    Console.WriteLine($"Period compilers available: {available}");
    Console.WriteLine(
        $"Totals: {results.Count} group placements, " +
        string.Join(", ", order
            .Select(o => (Outcome: o, Count: results.Count(r => r.Outcome == o)))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Count} {x.Outcome}")));
}

// Walk down to the nearest rung the compiler can actually be held to. C# 1.2 is a corpus row
// (foreach disposal) with no /langversion spelling of its own, so it is skipped as a floor.
static string? Below(string version, LanguageProfile profile)
{
    var index = LadderIndex(profile.Ladder, version);
    if (index <= 0)
    {
        return null;
    }

    for (var i = index - 1; i >= 0; i--)
    {
        if (profile.LangVersionArg(profile.Ladder[i]) is not null)
        {
            return profile.Ladder[i];
        }
    }

    return null;
}

// IReadOnlyList<T> has no IndexOf; the ladder is short enough that a linear scan costs nothing.
static int LadderIndex(IReadOnlyList<string> ladder, string version)
{
    for (var i = 0; i < ladder.Count; i++)
    {
        if (ladder[i] == version)
        {
            return i;
        }
    }

    return -1;
}

static string? LangVersionArg(string version) => version switch
{
    "1.0" => "ISO-1",
    "1.2" => null,
    "2.0" => "ISO-2",
    _ => version,
};

// Groups a floor probe cannot speak to, each for a reason inherent to the row rather than to the
// example's quality. Without these the probe reports NOT-VERSION-SPECIFIC and accuses code that is
// exactly what it should be.
static string? ExemptionReason(string group) => group switch
{
    "LockStatement" =>
        "the lock statement shipped in C# 1.0; the corpus files it under C# 3.0 to mirror its "
        + "source document's section placement, which MANIFEST.md's Note column records. An older "
        + "compiler accepting it is the expected result, not a defect",

    "EmbeddedInteropTypes" =>
        "NoPIA is a property of the reference (/link with EmbedInteropTypes), not of the source. "
        + "This harness compiles with /reference: only, so the feature is absent from the "
        + "compilation it performs and no compiler version can reveal it",

    _ => null,
};

// Failures that say something about the toolchain rather than the language. Treating one of these
// as a rejection would manufacture an UNGATED verdict out of a broken reference set.
static bool IsEnvironmentError(string diagnostic)
{
    string[] codes =
    [
        "CS0006",  // metadata file could not be found
        "CS1703",  // an assembly with the same identity was already imported
        "CS1704",  // an assembly with the same simple name was already imported
        "CS1705",  // assembly requires a newer version of a referenced assembly
        "CS2001",  // source file could not be found
        "CS2008",  // no source files specified
    ];

    return codes.Any(code => diagnostic.Contains(code, StringComparison.Ordinal));
}

// The pre-Roslyn compiler accepts only ISO-1, ISO-2, 3, 4, 5 -- not the "N.0" spellings Roslyn
// added. Anything above its ceiling has no answer here.
static string? LegacyLangVersionArg(string version) => version switch
{
    "1.0" => "ISO-1",
    "2.0" => "ISO-2",
    "3.0" => "3",
    "4.0" => "4",
    "5.0" => "5",
    _ => null,
};

static string? ParseFolderVersion(string folderName)
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

    return Versions.Ladder.Contains(candidate) ? candidate : null;
}

static string? ReadLangVersion(string csproj)
{
    foreach (var line in File.ReadLines(csproj))
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

static string HashFiles(string[] files)
{
    using var sha = SHA256.Create();
    var buffer = new StringBuilder();
    foreach (var file in files)
    {
        buffer.Append(Path.GetFileName(file));
        buffer.Append('\0');
        buffer.Append(File.ReadAllText(file));
        buffer.Append('\0');
    }

    return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(buffer.ToString())));
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

    var output = Run(vswhere,
        "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe");

    return output
        .Split('\n')
        .Select(line => line.Trim())
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

static class Versions
{
    // The last Roslyn that tops out at C# 6, used as the period compiler for that boundary.
    public const string CompilerPackage = "1.3.2";

    public static readonly List<string> Ladder = new()
    {
        "1.0", "1.2", "2.0", "3.0", "4.0", "5.0", "6.0",
        "7.0", "7.1", "7.2", "7.3",
        "8.0", "9.0", "10.0", "11.0", "12.0", "13.0", "14.0",
    };
}

// A language's identity within this probe: how to find its files, spell its language versions on
// the compiler command line, and walk its version ladder. C# is the only implementation today.
record LanguageProfile(
    string Name,
    string SourceExtension,
    string ProjectExtension,
    IReadOnlyList<string> Ladder,
    Func<string, string?> FolderVersion,
    Func<string, string?> LangVersionArg,
    Func<string, bool> IsEnvironmentError);

record Result(
    string Project,
    string Ceiling,
    string FeatureVersion,
    string Group,
    string Outcome,
    string Detail);

record Verdict(string Outcome, string Detail);

record CompileResult(bool Succeeded, string FirstError);
