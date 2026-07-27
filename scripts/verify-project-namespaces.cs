#!/usr/bin/env dotnet
#:property PublishAot=false

// Fails when a corpus source file's namespace does not name the project it lives in.
//
// Every corpus project is one coordinate: an SDK, a target framework, a <LangVersion>, and an
// output kind. That coordinate IS the content — the same feature sample means something different
// in Net48_CSharp5_Library than in Net10_CSharpLatest_Library — so a file must say which project it
// belongs to on the line an editor shows first. Each project sets <RootNamespace> to its
// coordinate, and every C# file under it declares a namespace whose first segment is that value.
//
// In C# that pairing is not automatic: <RootNamespace> only seeds new-file templates, it does not
// wrap existing files. So the two drift silently, which is exactly what happened before this guard
// existed — six net10 library projects all declared CSharpNet10Latest, ten net48 projects all
// declared CSharpFw48Cs73, and nothing was wrong enough to fail a build. A file copied into a
// sibling project compiles perfectly while claiming the wrong coordinate.
//
// VB is checked for <RootNamespace>, plus the inverse of the C# rule. A VB compilation prepends
// RootNamespace to every declaration in the project, so its .vb files declare version-relative
// namespaces (Namespace Vb15.Tuples) and inherit the coordinate at compile time — that is also what
// lets the VB families share one source tree across every pinned project. A file that also declares
// the project prefix compiles to a doubled namespace (Net10_Vb14_Library.Net10_Vb14_Library.Vb14.X),
// and because the tree is shared, one such file corrupts every pin that globs it at once.
//
// Run from repo root:
//   dotnet scripts/verify-project-namespaces.cs
//   dotnet scripts/verify-project-namespaces.cs -- --json
//
// Exit code is 1 when anything is found, so this is usable as a CI or pre-commit gate.

using System.Text.Json;
using System.Text.RegularExpressions;

#nullable enable

var emitJson = false;

foreach (var argument in args)
{
    if (argument == "--json")
        emitJson = true;
    else
    {
        Console.Error.WriteLine($"Unknown argument: {argument}");
        Console.Error.WriteLine("Usage: dotnet scripts/verify-project-namespaces.cs -- [--json]");
        return 2;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Could not locate repo root (no .git directory found).");
var corpusRoot = Path.Combine(repoRoot, "examples", "language-features");

// C# files that legitimately declare no namespace. Listed rather than pattern-skipped: a silent
// skip rule grows to cover real drift, and each of these is exempt for a reason worth naming.
var exempt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // Top-level statements cannot sit inside a namespace — that is the feature. The three exe
    // projects are distinguished by <RootNamespace> alone.
    ["CSharp/dotnet/10/9.0/exe/CSharp9/TopLevelStatements/TopLevelStatements.cs"] = "top-level statements",
    ["CSharp/dotnet/10/14.0/exe/CSharp9/TopLevelStatements/TopLevelStatements.cs"] = "top-level statements",
    ["CSharp/dotnet/10/latest/exe/CSharp9/TopLevelStatements/TopLevelStatements.cs"] = "top-level statements",

    // Assembly-level attributes only; there is nothing to place in a namespace.
    ["CSharp/dotNetFramework/v4.8/CSharp_v1.0/Properties/AssemblyInfo.cs"] = "assembly attributes only",
};

// CSharpComTypeLib is a support assembly for the NoPIA row, not a coordinate in the corpus, so its
// namespace is its own name rather than a runtime/language pairing. Nineteen samples across the
// net48 projects consume it by that name.
var supportProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "CSharp/CSharpComTypeLib/CSharpComTypeLib.csproj",
};

var rootNamespacePattern = new Regex(@"<RootNamespace>\s*(?<value>[^<\s][^<]*?)\s*</RootNamespace>");
var startupObjectPattern = new Regex(@"<StartupObject>\s*(?<value>[^<\s][^<]*?)\s*</StartupObject>");
var namespacePattern = new Regex(@"^namespace\s+(?<head>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline);
var vbNamespacePattern = new Regex(@"^\s*Namespace\s+(?<head>[A-Za-z_][A-Za-z0-9_]*)",
    RegexOptions.Multiline | RegexOptions.IgnoreCase);

var findings = new List<Finding>();
var projectsChecked = 0;
var filesChecked = 0;
var seenExempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var scannedVbSourceRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

var projectFiles = Directory
    .EnumerateFiles(corpusRoot, "*.*proj", SearchOption.AllDirectories)
    .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
    .Where(path => !IsUnderBuildOutput(corpusRoot, path))
    .OrderBy(path => path, StringComparer.Ordinal)
    .ToList();

foreach (var projectPath in projectFiles)
{
    var projectRelative = Relative(corpusRoot, projectPath);
    projectsChecked++;

    // ---------------------------------------------------------------------------------------
    // Rule 1 — every corpus project declares a <RootNamespace>.
    //
    // Without one the SDK falls back to the project file's own name, and eleven projects in this
    // tree are named library.csproj. The fallback is therefore not merely implicit, it collides.
    // ---------------------------------------------------------------------------------------
    var projectText = File.ReadAllText(projectPath);
    var rootNamespaceMatch = rootNamespacePattern.Match(projectText);
    if (!rootNamespaceMatch.Success)
    {
        findings.Add(new Finding("missing-root-namespace", projectRelative,
            "no <RootNamespace>; the SDK would fall back to the project file name, which is not unique in this tree"));
        continue;
    }

    var rootNamespace = rootNamespaceMatch.Groups["value"].Value;

    // ---------------------------------------------------------------------------------------
    // Rule 1b (VB only) — no .vb source under a family's shared src/ tree redeclares the
    // project prefix.
    //
    // VB inherits <RootNamespace> at compile time, so a source file must NOT also declare it. A
    // file that does compiles to a doubled namespace, and because the VB families share one
    // source tree across every pinned project, a single such file corrupts every pin that globs
    // it at once. Scan each family's src/ tree the first time a project in that family is
    // visited, not once per project — the same file belongs to many projects, and reporting it
    // once per pin would bury the finding under duplicates.
    // ---------------------------------------------------------------------------------------
    if (projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
    {
        var familySrcRoot = FindFamilySrcRoot(corpusRoot, Path.GetDirectoryName(projectPath)!);
        if (familySrcRoot is not null && scannedVbSourceRoots.Add(familySrcRoot))
        {
            var vbSources = Directory
                .EnumerateFiles(familySrcRoot, "*.vb", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutput(corpusRoot, path))
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var source in vbSources)
            {
                filesChecked++;
                var sourceRelative = Relative(corpusRoot, source);

                // Check every namespace declaration in the file, not just the first — the same
                // mirroring mistake as Rule 3's `.Matches` over `.Match` for C#. A file with a clean
                // first Namespace block and a project-prefixed second one must not pass silently.
                foreach (var declared in NamespaceSegments(vbNamespacePattern, source))
                {
                    if (declared.StartsWith("Net10_", StringComparison.Ordinal) ||
                        declared.StartsWith("Net48_", StringComparison.Ordinal))
                    {
                        findings.Add(new Finding(
                            "vb-project-prefixed-namespace",
                            sourceRelative,
                            $"declares namespace {declared}, but VB prepends <RootNamespace> itself; this "
                            + "compiles to a doubled namespace and corrupts every pin sharing this source"));
                    }
                }
            }
        }

        continue;
    }

    // Support assemblies are not coordinates, but still have to declare a RootNamespace, which
    // Rule 1 above just checked.
    if (supportProjects.Contains(projectRelative))
    {
        continue;
    }

    // ---------------------------------------------------------------------------------------
    // Rule 2 — a <StartupObject> names a type in this project's namespace.
    //
    // It is the other property that hard-codes the root namespace, and it fails late and
    // obscurely: the compiler reports CS1555 ("Could not find 'X.Program' specified for Main
    // method") with no hint that a namespace moved out from under it.
    // ---------------------------------------------------------------------------------------
    var startupObjectMatch = startupObjectPattern.Match(projectText);
    if (startupObjectMatch.Success)
    {
        var startupObject = startupObjectMatch.Groups["value"].Value;
        var startupHead = startupObject.Split('.')[0];
        if (startupHead != rootNamespace)
        {
            findings.Add(new Finding("wrong-startup-object", projectRelative,
                $"<StartupObject>{startupObject}</StartupObject> starts with {startupHead}, but this project's namespace is {rootNamespace}"));
        }
    }

    // ---------------------------------------------------------------------------------------
    // Rule 3 — every C# file under the project declares that namespace as its first segment.
    // ---------------------------------------------------------------------------------------
    var projectDirectory = Path.GetDirectoryName(projectPath)!;
    var sourceFiles = Directory
        .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
        .Where(path => !IsUnderBuildOutput(corpusRoot, path))
        .OrderBy(path => path, StringComparer.Ordinal);

    foreach (var sourcePath in sourceFiles)
    {
        var sourceRelative = Relative(corpusRoot, sourcePath);
        filesChecked++;

        var matches = namespacePattern.Matches(File.ReadAllText(sourcePath));
        var isExempt = exempt.ContainsKey(sourceRelative);
        if (isExempt)
            seenExempt.Add(sourceRelative);

        if (matches.Count == 0)
        {
            if (!isExempt)
            {
                findings.Add(new Finding("no-namespace", sourceRelative,
                    $"declares no namespace, so nothing in it names {rootNamespace}; add one or list the file as exempt in this script"));
            }

            continue;
        }

        if (isExempt)
        {
            findings.Add(new Finding("stale-exemption", sourceRelative,
                $"listed exempt in this script ({exempt[sourceRelative]}) but now declares a namespace; drop the exemption"));
            continue;
        }

        foreach (var head in matches.Select(match => match.Groups["head"].Value).Distinct(StringComparer.Ordinal))
        {
            if (head != rootNamespace)
            {
                findings.Add(new Finding("wrong-namespace", sourceRelative,
                    $"namespace starts with {head}, but this file belongs to {rootNamespace} ({projectRelative})"));
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Rule 4 — the exempt list names files that exist.
//
// An exemption whose file has been renamed or deleted is a hole nobody is watching, so it fails
// the run rather than being ignored.
// ---------------------------------------------------------------------------------------------
foreach (var (path, reason) in exempt)
{
    if (!seenExempt.Contains(path))
    {
        findings.Add(new Finding("orphan-exemption", path,
            $"exempt in this script ({reason}) but no such file was scanned; remove the entry"));
    }
}

if (emitJson)
{
    Console.WriteLine(JsonSerializer.Serialize(
        new { projectsChecked, filesChecked, findings },
        new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    foreach (var finding in findings)
        Console.WriteLine($"{finding.Rule}: {finding.Path}\n    {finding.Detail}");

    Console.WriteLine(findings.Count == 0
        ? $"verify-project-namespaces: {projectsChecked} project(s), {filesChecked} file(s), no findings."
        : $"verify-project-namespaces: {projectsChecked} project(s), {filesChecked} file(s), {findings.Count} finding(s).");
}

return findings.Count == 0 ? 0 : 1;

static bool IsUnderBuildOutput(string corpusRoot, string path)
{
    var relative = Relative(corpusRoot, path);
    return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
        || relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
}

static string Relative(string root, string path) =>
    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

// Walks up from a project's directory to the nearest ancestor holding a src/ subdirectory — that
// ancestor is the VB family root (e.g. .../VB.NET/dotnet/Net10), and src/ is the tree its pinned
// projects all glob. Bounded by corpusRoot so a family without a src/ tree cannot fall through to
// an unrelated directory of the same name elsewhere in the repository.
static string? FindFamilySrcRoot(string corpusRoot, string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && dir.FullName.Length >= corpusRoot.Length)
    {
        var candidate = Path.Combine(dir.FullName, "src");
        if (Directory.Exists(candidate))
            return candidate;

        dir = dir.Parent;
    }

    return null;
}

static IEnumerable<string> NamespaceSegments(Regex pattern, string path) =>
    pattern.Matches(File.ReadAllText(path))
        .Select(match => match.Groups["head"].Value)
        .Distinct(StringComparer.Ordinal);

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

internal readonly record struct Finding(string Rule, string Path, string Detail);
