#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Derives the two net48 C# projects of the language-feature showcase from CSharpNet10Latest.
//
//   dotnet scripts/generate-net48-examples.cs               regenerate both projects
//   dotnet scripts/generate-net48-examples.cs -- --check    verify the committed output matches
//
// Flags (pass them after `--`):
//   --check   Re-derive in memory and report any drift instead of writing. Exit 1 if the tree
//             on disk differs from what a regeneration would produce.
//
// The `--` is load-bearing whenever a flag is passed -- `dotnet <file>.cs` forwards only the
// arguments it does not claim for itself, and which ones it claims grows between SDK versions.
//
// WHY THIS EXISTS
//
// CSharpFw48Cs73 (75 rows) and CSharpFw48Cs80 (87 rows) hold ~166 files whose only difference
// from their CSharpNet10Latest originals is the root namespace. That was established by probe,
// not assumed: copying every C# 1.0-8.0 sample into a net48 project and rewriting only the
// namespace compiles at 0 errors and 0 warnings on both project formats, given the shim packages
// each project already declares. No sample needs a net48-specific variant.
//
// Hand-copying would therefore create 166 near-duplicates with no mechanism to keep them in sync.
// Every correction made while authoring this corpus had to be applied to each copy of the affected
// sample; that held at n=2 and does not at n=166. One source of truth plus a derivation keeps a
// fix to a shared sample a one-file edit.
//
// The generated files ARE committed -- the corpus is documentation meant to be read in git, not a
// build artifact. `--check` is what makes that safe: it re-derives and fails on drift, so a
// hand-edit to a generated copy is caught rather than silently reverted by the next regeneration.
// Edit CSharpNet10Latest and regenerate; never edit a generated file.
//
// WHAT THE TWO PROJECTS DO NOT SHARE WITH THE SOURCE
//
// Only their project files, which this script does not own except for CSharpFw48Cs73's <Compile>
// list. That project is legacy non-SDK XML and has no globbing, so its file list must be written
// out item by item; it is regenerated between the marker comments in the .csproj and nothing else
// in that file is touched.

using System.Text;
using static Config;

var check = false;
foreach (var arg in args)
{
    switch (arg)
    {
        case "--check":
            check = true;
            break;
        case "-h" or "--help":
            Console.WriteLine("Usage: dotnet scripts/generate-net48-examples.cs [-- --check]");
            return 0;
        default:
            Console.Error.WriteLine($"generate-net48-examples: unknown argument: {arg}");
            Console.Error.WriteLine("Usage: dotnet scripts/generate-net48-examples.cs [-- --check]");
            return 1;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
if (repoRoot is null)
{
    Console.Error.WriteLine("generate-net48-examples: could not locate the repo root.");
    return 1;
}

var corpusRoot = Path.Combine(repoRoot, "examples", "language-features");

var drift = new List<string>();
var written = 0;

foreach (var target in Targets)
{
    var sourceProject = Path.Combine(corpusRoot, target.SourceProjectName);
    if (!Directory.Exists(sourceProject))
    {
        Console.Error.WriteLine($"generate-net48-examples: source project not found at '{sourceProject}'.");
        return 1;
    }

    var targetRoot = Path.Combine(corpusRoot, target.ProjectName);
    var plan = BuildPlan(sourceProject, target);

    if (plan.Count == 0)
    {
        Console.Error.WriteLine(
            $"generate-net48-examples: {target.ProjectName} derived zero files -- the source project's " +
            "version folders are missing or renamed. Refusing to continue.");
        return 1;
    }

    if (check)
    {
        drift.AddRange(FindDrift(targetRoot, target, plan));
        Console.WriteLine($"  checked  {target.ProjectName,-18} {plan.Count} files");
    }
    else
    {
        PruneManagedFolders(targetRoot, target);
        foreach (var (relativePath, content) in plan)
        {
            var destination = Path.Combine(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, content);
            written++;
        }
        Console.WriteLine($"  wrote    {target.ProjectName,-18} {plan.Count} files");
    }

    if (target.CompileItemsProjectFile is { } projectFileName)
    {
        var projectFile = Path.Combine(targetRoot, projectFileName);
        var updated = RenderCompileItems(projectFile, plan.Select(entry => entry.RelativePath));
        if (updated is null)
        {
            Console.Error.WriteLine(
                $"generate-net48-examples: could not find the compile-item markers in '{projectFile}'. " +
                $"Expected a line containing '{CompileItemsBeginMarker}' and one containing " +
                $"'{CompileItemsEndMarker}'.");
            return 1;
        }

        var current = File.ReadAllText(projectFile);
        if (!string.Equals(current, updated, StringComparison.Ordinal))
        {
            if (check)
                drift.Add($"{target.ProjectName}/{projectFileName}: <Compile> item list is stale");
            else
                File.WriteAllText(projectFile, updated);
        }
    }
}

if (check)
{
    if (drift.Count > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"generate-net48-examples: {drift.Count} file(s) drifted from the source:");
        foreach (var entry in drift.Take(DriftReportLimit))
            Console.Error.WriteLine($"  {entry}");
        if (drift.Count > DriftReportLimit)
            Console.Error.WriteLine($"  ... and {drift.Count - DriftReportLimit} more");
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "The net48 projects are derived from their net10 counterparts. Make the fix there and re-run");
        Console.Error.WriteLine("  dotnet scripts/generate-net48-examples.cs");
        Console.Error.WriteLine("rather than editing a generated file.");
        return 1;
    }

    Console.WriteLine();
    Console.WriteLine("generate-net48-examples: no drift.");
    return 0;
}

Console.WriteLine();
Console.WriteLine($"generate-net48-examples: wrote {written} files across {Targets.Length} projects.");
return 0;

// ── Derivation ──────────────────────────────────────────────────────────────

// Every generated file is the source file with its root namespace rewritten. The rewrite is a
// whole-word replacement of the source project name, which appears only in the namespace
// declaration and never as a substring of any other identifier in this corpus.
static List<(string RelativePath, string Content)> BuildPlan(string sourceProject, Target target)
{
    var plan = new List<(string, string)>();

    foreach (var versionFolder in target.VersionFolders)
    {
        var sourceFolder = Path.Combine(sourceProject, versionFolder);
        if (!Directory.Exists(sourceFolder))
            continue;

        var files = Directory
            .EnumerateFiles(sourceFolder, target.FilePattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !target.ExcludedGroups.Contains(GroupFolderOf(sourceFolder, path)))
            .Where(path => !target.HandAuthoredGroups.Contains(GroupFolderOf(sourceFolder, path)))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(sourceProject, file);
            var content = File.ReadAllText(file)
                .Replace(target.SourceProjectName, target.ProjectName, StringComparison.Ordinal);
            plan.Add((relativePath, content));
        }
    }

    return plan;
}

static string GroupFolderOf(string versionFolder, string filePath)
{
    var relative = Path.GetRelativePath(versionFolder, filePath);
    var separator = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
    return separator < 0 ? relative : relative[..separator];
}

static bool IsBuildOutput(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.Contains("/obj/", StringComparison.Ordinal)
        || normalized.Contains("/bin/", StringComparison.Ordinal);
}

// ── Drift detection ─────────────────────────────────────────────────────────

// Drift is symmetric: a generated file that is missing, one whose content no longer matches the
// source, and a stray file sitting in a managed folder are all reported. The third case matters --
// a hand-added example in a generated project would otherwise pass every check while being
// invisible to the source of truth.
static List<string> FindDrift(string targetRoot, Target target, List<(string RelativePath, string Content)> plan)
{
    var findings = new List<string>();
    var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var (relativePath, content) in plan)
    {
        expected.Add(relativePath);
        var destination = Path.Combine(targetRoot, relativePath);

        if (!File.Exists(destination))
        {
            findings.Add($"{target.ProjectName}/{relativePath}: missing");
            continue;
        }

        if (!string.Equals(File.ReadAllText(destination), content, StringComparison.Ordinal))
            findings.Add($"{target.ProjectName}/{relativePath}: differs from the source sample");
    }

    foreach (var versionFolder in target.VersionFolders)
    {
        var folder = Path.Combine(targetRoot, versionFolder);
        if (!Directory.Exists(folder))
            continue;

        var strays = Directory
            .EnumerateFiles(folder, target.FilePattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !target.HandAuthoredGroups.Contains(GroupFolderOf(folder, path)))
            .Select(path => Path.GetRelativePath(targetRoot, path))
            .Where(relative => !expected.Contains(relative))
            .OrderBy(relative => relative, StringComparer.Ordinal);

        foreach (var stray in strays)
            findings.Add($"{target.ProjectName}/{stray}: not derived from {target.SourceProjectName}");
    }

    return findings;
}

// Only the group folders this script derives are cleared. A hand-authored group inside a managed
// version folder survives, as does anything outside the version folders -- the project file, a
// README, a folder a later plan adds by hand.
static void PruneManagedFolders(string targetRoot, Target target)
{
    foreach (var versionFolder in target.VersionFolders)
    {
        var folder = Path.Combine(targetRoot, versionFolder);
        if (!Directory.Exists(folder))
            continue;

        foreach (var groupFolder in Directory.EnumerateDirectories(folder))
        {
            if (!target.HandAuthoredGroups.Contains(Path.GetFileName(groupFolder)))
                Directory.Delete(groupFolder, recursive: true);
        }

        foreach (var looseFile in Directory.EnumerateFiles(folder, target.FilePattern))
            File.Delete(looseFile);
    }
}

// ── Legacy project <Compile> items ──────────────────────────────────────────

// CSharpFw48Cs73 is non-SDK XML and has no globbing, so every file needs an explicit item. The
// list is rewritten between the marker comments; the rest of the project file is preserved
// byte-for-byte, including whatever line ending it already uses.
static string? RenderCompileItems(string projectFile, IEnumerable<string> relativePaths)
{
    if (!File.Exists(projectFile))
        return null;

    var original = File.ReadAllText(projectFile);
    var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = original.Split(newline);

    var begin = Array.FindIndex(lines, line => line.Contains(CompileItemsBeginMarker, StringComparison.Ordinal));
    var end = Array.FindIndex(lines, line => line.Contains(CompileItemsEndMarker, StringComparison.Ordinal));
    if (begin < 0 || end < 0 || end < begin)
        return null;

    // The begin marker must close its own XML comment. If it does not, everything this method
    // writes after it lands *inside* that comment -- XML comments do not nest, so the run would
    // not terminate until the end marker's own `-->`. The project would still build, with zero
    // source files and no error, which is the worst possible way for this to fail.
    if (!lines[begin].Contains("-->", StringComparison.Ordinal))
        return null;

    var rebuilt = new StringBuilder();
    for (var i = 0; i <= begin; i++)
        rebuilt.Append(lines[i]).Append(newline);

    foreach (var relativePath in relativePaths.OrderBy(p => p, StringComparer.Ordinal))
        rebuilt.Append("    <Compile Include=\"").Append(relativePath.Replace('/', '\\')).Append("\" />").Append(newline);

    for (var i = end; i < lines.Length; i++)
    {
        rebuilt.Append(lines[i]);
        if (i < lines.Length - 1)
            rebuilt.Append(newline);
    }

    return rebuilt.ToString();
}

// ── Configuration ───────────────────────────────────────────────────────────

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
            File.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

// HandAuthoredGroups names group folders the target owns itself. They are neither derived nor
// pruned nor reported as strays: a row can be hand-authored either because the source project
// cannot carry it (MyNamespaceHelpers needs the `My` namespace, which net10.0 does not populate)
// or because its net48 form genuinely differs from the net10 one.
public sealed record Target(
    string ProjectName,
    string SourceProjectName,
    string FilePattern,
    string[] VersionFolders,
    HashSet<string> ExcludedGroups,
    HashSet<string> HandAuthoredGroups,
    string? CompileItemsProjectFile);

public static class Config
{
    public const string CSharpSource = "CSharpNet10Latest";
    public const string VbSource = "VbNetNet10Latest";
    public const string CompileItemsBeginMarker = "GENERATED-COMPILE-ITEMS-BEGIN";
    public const string CompileItemsEndMarker = "GENERATED-COMPILE-ITEMS-END";
    public const int DriftReportLimit = 25;

    // Ordered oldest-first so the generated <Compile> list and the console output both read in
    // version order. CSharp1_2 sorts before CSharp2 here deliberately -- ordinal sorting would put
    // CSharp10 between CSharp1 and CSharp1_2.
    public static readonly string[] ThroughCSharp73 =
    [
        "CSharp1", "CSharp1_2", "CSharp2", "CSharp3", "CSharp4", "CSharp5",
        "CSharp6", "CSharp7", "CSharp7_1", "CSharp7_2", "CSharp7_3",
    ];

    public static readonly string[] VbVersionFolders =
    [
        "Baseline", "Vb14", "Vb15", "Vb15_3", "Vb15_5", "Vb16_0", "Vb16_9", "Vb17_13",
    ];

    public static readonly Target[] Targets =
    [
        // LangVersion 7.3 -- takes every row up to C# 7.3. The unsafe rows are not here to exclude:
        // they live in the *Unsafe projects and never appear in CSharpNet10Latest at all.
        new Target(
            ProjectName: "CSharpFw48Cs73",
            SourceProjectName: CSharpSource,
            FilePattern: "*.cs",
            VersionFolders: ThroughCSharp73,
            ExcludedGroups: new HashSet<string>(StringComparer.Ordinal),
            HandAuthoredGroups: new HashSet<string>(StringComparer.Ordinal),
            CompileItemsProjectFile: "CSharpFw48Cs73.csproj"),

        // LangVersion 8.0 -- adds CSharp8 minus the two capability exclusions recorded in
        // MANIFEST.md: default interface members need a runtime feature net48 never gained
        // (CS8701/CS8703), and System.Index/System.Range have no official net48 backport.
        new Target(
            ProjectName: "CSharpFw48Cs80",
            SourceProjectName: CSharpSource,
            FilePattern: "*.cs",
            VersionFolders: [.. ThroughCSharp73, "CSharp8"],
            ExcludedGroups: new HashSet<string>(StringComparer.Ordinal)
            {
                "DefaultInterfaceMembers",
                "RangesAndIndexes",
            },
            HandAuthoredGroups: new HashSet<string>(StringComparer.Ordinal),
            CompileItemsProjectFile: null),

        // VB needs no content rewrite at all: its samples declare version-relative namespaces
        // ("Namespace Vb15.Tuples") and never name their project, because VB prepends RootNamespace
        // from the project file. The copy is therefore byte-for-byte.
        new Target(
            ProjectName: "VbNetFw48",
            SourceProjectName: VbSource,
            FilePattern: "*.vb",
            VersionFolders: VbVersionFolders,
            ExcludedGroups: new HashSet<string>(StringComparer.Ordinal)
            {
                // Both consume attributes net48's BCL never had, with no official backport
                // package. Supplying them would mean re-declaring BCL types in a BCL namespace,
                // which the applicability rule's clause (2) rules out. Recorded as capability
                // exclusions in MANIFEST.md; both keep working examples in VbNetNet10Latest.
                "CallerArgumentExpressionConsumption",
                "OverloadResolutionPriorityConsumption",
            },
            HandAuthoredGroups: new HashSet<string>(StringComparer.Ordinal)
            {
                // The `My` namespace is populated on net48 and empty on net10.0, so this row has no
                // source-project counterpart to derive from -- it exists only here.
                "MyNamespaceHelpers",

                // The net10 sample's no-caveat half calls CollectionsMarshal.GetValueRefOrNullRef,
                // which has no net48 backport. The net48 form consumes a ref return from the
                // corpus's own C# project instead, which suits the row's name better anyway.
                "ConsumingCSharpRefReturnValues",
            },
            CompileItemsProjectFile: null),
    ];
}
