#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Query Roslyn and .NET BCL API XML docs from local submodules.
// Searches both external/roslyn-api-docs/ and external/dotnet-api-docs/ automatically.
// Works from the main repo checkout or any git worktree — locates the main checkout
// via git's commondir metadata so submodules only need to be initialized once.
//
// Run from repo root or any worktree directory:
//   dotnet scripts/api-docs-query.cs <TypeName>
//   dotnet scripts/api-docs-query.cs <TypeName>.<MemberName>
//   dotnet scripts/api-docs-query.cs --search <pattern>
//
// Examples:
//   dotnet scripts/api-docs-query.cs SymbolFinder
//   dotnet scripts/api-docs-query.cs SymbolFinder.FindCallersAsync
//   dotnet scripts/api-docs-query.cs IImmutableSet
//   dotnet scripts/api-docs-query.cs --search ImmutableSet

using System.Xml.Linq;

var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Could not locate repo root (no .git found).");

// When running from a git worktree the submodules may only be initialized in the main
// checkout. Resolve the main repo root via git's commondir metadata and search there
// first; fall back to the current worktree in case they were initialized locally too.
var mainRepoRoot = FindMainRepoRoot(repoRoot);

// All known XML doc roots — add new submodules here
var allRoots = new[]
{
    Path.Combine(mainRepoRoot, "external", "roslyn-api-docs", "dotnet", "xml"),
    Path.Combine(mainRepoRoot, "external", "dotnet-api-docs", "xml"),
    Path.Combine(repoRoot,     "external", "roslyn-api-docs", "dotnet", "xml"),
    Path.Combine(repoRoot,     "external", "dotnet-api-docs", "xml"),
};

var availableRoots = allRoots.Distinct().Where(Directory.Exists).ToArray();
if (availableRoots.Length == 0)
{
    Console.Error.WriteLine("ERROR: no API doc submodules found. Searched:");
    foreach (var r in allRoots.Distinct())
        Console.Error.WriteLine($"  {r}");
    Console.Error.WriteLine("Run: git submodule update --init external/roslyn-api-docs external/dotnet-api-docs");
    return 1;
}

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

if (args[0] == "--search")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("--search requires a pattern argument");
        return 1;
    }
    var pattern = args[1];
    var found = false;
    foreach (var root in availableRoots)
    {
        foreach (var nsDir in Directory.EnumerateDirectories(root).Order())
        {
            foreach (var file in Directory.EnumerateFiles(nsDir, "*.xml").Order())
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {Path.GetFileName(nsDir)}.{stem}");
                    found = true;
                }
            }
        }
    }
    if (!found)
        Console.WriteLine($"No types found matching '{pattern}'");
    return 0;
}

var query = args[0];
string typeName;
string? memberName = null;

if (query.Contains('.'))
{
    var dot = query.LastIndexOf('.');
    typeName = query[..dot];
    memberName = query[(dot + 1)..];
}
else
{
    typeName = query;
}

var xmlFiles = availableRoots.SelectMany(r => FindTypeXml(r, typeName)).ToList();
if (xmlFiles.Count == 0)
{
    Console.Error.WriteLine($"Type '{typeName}' not found in any available doc root.");
    Console.Error.WriteLine("Try: dotnet scripts/api-docs-query.cs --search <partial-name>");
    return 1;
}

foreach (var file in xmlFiles)
    ShowMembers(file, memberName);

return 0;

// ── helpers ──────────────────────────────────────────────────────────────────

static List<string> FindTypeXml(string docsRoot, string typeName)
{
    var results = new List<string>();
    foreach (var nsDir in Directory.EnumerateDirectories(docsRoot))
    {
        // Exact match first (non-generic)
        var exact = Path.Combine(nsDir, typeName + ".xml");
        if (File.Exists(exact))
        {
            results.Add(exact);
            continue;
        }
        // Generic types: file is named TypeName`N.xml — match any arity
        results.AddRange(
            Directory.EnumerateFiles(nsDir, typeName + "`*.xml"));
    }
    return results;
}

static void ShowMembers(string xmlPath, string? filterMember)
{
    var doc = XDocument.Load(xmlPath);
    var root = doc.Root!;
    var fullName = root.Attribute("FullName")?.Value ?? root.Attribute("Name")?.Value
        ?? Path.GetFileNameWithoutExtension(xmlPath);
    Console.WriteLine($"# {fullName}");
    Console.WriteLine();

    var members = root.Descendants("Member")
        .Where(m => filterMember is null || m.Attribute("MemberName")?.Value == filterMember)
        .ToList();

    if (members.Count == 0)
    {
        Console.WriteLine($"  (no members matching '{filterMember}')");
        return;
    }

    foreach (var member in members)
    {
        // Prefer the last C# signature (newest, typically has nullable annotations)
        var csharpSig = member.Elements("MemberSignature")
            .LastOrDefault(e => e.Attribute("Language")?.Value == "C#")
            ?.Attribute("Value")?.Value;
        if (csharpSig is null)
            continue;

        Console.WriteLine($"  {csharpSig}");

        var docs = member.Element("Docs");
        if (docs is not null)
        {
            var summary = docs.Element("summary")?.Value?.Trim();
            if (!IsPlaceholder(summary))
                Console.WriteLine($"  Summary: {summary}");

            foreach (var param in member.Element("Parameters")?.Elements("Parameter") ?? [])
            {
                var pName = param.Attribute("Name")?.Value ?? "";
                var pDoc = docs.Elements("param")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == pName)
                    ?.Value?.Trim();
                if (!IsPlaceholder(pDoc))
                    Console.WriteLine($"  Param  {pName}: {pDoc}");
            }

            var remarks = docs.Element("remarks")?.Value?.Trim();
            if (!IsPlaceholder(remarks))
                Console.WriteLine($"  Remarks: {remarks}");
        }

        Console.WriteLine();
    }
}

static bool IsPlaceholder(string? text) =>
    string.IsNullOrWhiteSpace(text) || text.Trim() == "To be added.";

static void PrintHelp() => Console.WriteLine("""
    Usage:
      dotnet scripts/api-docs-query.cs <TypeName>               List all members
      dotnet scripts/api-docs-query.cs <TypeName>.<MemberName>  Show specific overloads
      dotnet scripts/api-docs-query.cs --search <pattern>       Find types by name fragment

    Searches both external/roslyn-api-docs/ and external/dotnet-api-docs/ automatically.

    Examples:
      dotnet scripts/api-docs-query.cs SymbolFinder
      dotnet scripts/api-docs-query.cs SymbolFinder.FindCallersAsync
      dotnet scripts/api-docs-query.cs IImmutableSet
      dotnet scripts/api-docs-query.cs --search ImmutableSet
    """);

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        var git = Path.Combine(dir.FullName, ".git");
        if (Directory.Exists(git) || File.Exists(git))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

// Returns the main repo root. In a git worktree the local .git is a file pointing at
// the shared git state; we follow the commondir pointer to find the main checkout.
static string FindMainRepoRoot(string repoRoot)
{
    var gitPath = Path.Combine(repoRoot, ".git");

    if (Directory.Exists(gitPath))
        return repoRoot; // already the main checkout

    if (!File.Exists(gitPath))
        return repoRoot; // unexpected layout — give up

    var line = File.ReadAllText(gitPath).Trim();
    if (!line.StartsWith("gitdir: "))
        return repoRoot;

    var worktreeGitDir = line["gitdir: ".Length..].Trim();
    if (!Path.IsPathRooted(worktreeGitDir))
        worktreeGitDir = Path.GetFullPath(Path.Combine(repoRoot, worktreeGitDir));

    var commondirFile = Path.Combine(worktreeGitDir, "commondir");
    if (!File.Exists(commondirFile))
        return repoRoot;

    var commonRel = File.ReadAllText(commondirFile).Trim();
    var commonDir = Path.IsPathRooted(commonRel)
        ? commonRel
        : Path.GetFullPath(Path.Combine(worktreeGitDir, commonRel));

    // commonDir == /path/to/main/.git  →  parent is the main repo root
    return Path.GetDirectoryName(commonDir) ?? repoRoot;
}
