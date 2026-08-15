#!/usr/bin/env dotnet
#:property PublishAot=false

// Fails when upstream third-party content has been committed to this repository.
//
// This repository is MIT-licensed and every tracked file is authored here. The upstream Microsoft
// repositories in sources.json are FETCHED at runtime into a per-user cache outside any working
// tree — they are never vendored, never submoduled, and never redistributed. That is what keeps
// the MIT grant in LICENSE truthful: it is a statement about content we wrote, and it silently
// becomes false the moment somebody else's content lands beside it.
//
// The failure this guards against is quiet. A cache pointed at the repo for debugging, a submodule
// re-added out of habit, a spec section pasted into a doc "for convenience" — none of these look
// like a licensing decision at the time, and none of them announce themselves in review.
//
// Run from repo root:
//   dotnet scripts/verify-no-vendored-content.cs
//   dotnet scripts/verify-no-vendored-content.cs -- --json
//   dotnet scripts/verify-no-vendored-content.cs -- --history
//
// By default only the tracked tree is checked, because that is what a release ships. --history
// additionally searches every blob in every commit: content deleted in a later commit is still
// distributed with the repository, so a clean working tree does not by itself mean a clean clone.
//
// Exit code is 1 when anything is found, so this is usable as a CI or pre-commit gate.
// See THIRD-PARTY-NOTICES.md for the policy this enforces.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// scripts/Directory.Build.props turns Nullable off for utility scripts; this file uses nullable
// annotations, so it opts itself back in rather than emitting CS8632 on every one of them.
#nullable enable

var emitJson = false;
var scanHistory = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--json")
        emitJson = true;
    else if (args[i] == "--history")
        scanHistory = true;
    else
    {
        Console.Error.WriteLine($"Unknown argument: {args[i]}");
        Console.Error.WriteLine("Usage: dotnet scripts/verify-no-vendored-content.cs -- [--json] [--history]");
        return 2;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Could not locate repo root (no .git directory found).");

// Files exempt from the content scans below, because their job is to *name* the things those scans
// look for. Without this, the guard reports itself. Keep this list as short as it is today: an
// entry here is a file nobody is checking, so it must be one whose whole purpose is the policy.
var contentScanExempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "scripts/verify-no-vendored-content.cs",
    "THIRD-PARTY-NOTICES.md",
};

// Extensions exempt from the SHAPE patterns (rule 4) only. The header patterns (rule 3) still run
// on every file, because a pasted copyright notice is a finding wherever it lands.
//
// Rule 4's premise is that our own writing cannot produce an upstream document's scaffolding by
// accident. That premise holds for prose and for data files, and fails for source code: a test
// covering a parser has to build the very shape the parser reads. ApiDocsQueryServiceTests.cs
// constructs ECMA-XML for a fictional System.Widget, and rule 4 could not tell it from a vendored
// dotnet-api-docs page — nor could any structural narrowing, because a good fixture is structurally
// a real document in miniature.
//
// The discriminator is the file type, not the content. Vendored material arrives as a file of its
// own kind: dotnet-api-docs ships .xml, csharplang ships .md. Getting one into a .cs string literal
// means escaping every quote and re-indenting it by hand, which is authoring rather than copying.
// Excluding a whole file from the scan would blind the guard; excluding source files from one
// heuristic that cannot apply to them keeps every other check pointed at them.
var shapeScanExemptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".cs",
    ".csx",
    ".vb",
};

var findings = new List<Finding>();

// ---------------------------------------------------------------------------------------------
// Rule 1 — the fetch cache must stay ignored.
//
// sources.json's cache normally lives outside any repository, but the design deliberately allows
// pointing it at the repo for debugging. .gitignore is the only thing standing between that and a
// commit of several hundred megabytes of someone else's documentation, so a regression in those
// entries is checked directly rather than assumed.
// ---------------------------------------------------------------------------------------------
foreach (var probe in new[] { "sources/probe.txt", ".sources/probe.txt" })
{
    if (!IsIgnored(repoRoot, probe))
    {
        findings.Add(new Finding(
            "cache-not-ignored",
            ".gitignore",
            $"'{probe}' is not ignored. Restore the /sources/ and /.sources/ entries in .gitignore "
                + "before fetching anything into the working tree."));
    }
}

// ---------------------------------------------------------------------------------------------
// Rule 2 — no tracked file may sit inside an upstream tree.
//
// Two sets, because they carry different risks of collision with our own code.
//
// Root-anchored: 'sources' and 'external' are ordinary English words that legitimately name our
// own directories — src/DotNetKnowledge.Mcp/Sources/ is the server's own namespace. Only the
// repo-root forms are cache locations, which is exactly what .gitignore's /sources/ and
// /.sources/ entries say. Matching them at any depth reports the server's own code as vendored,
// and a guard that cries wolf is a guard somebody switches off.
//
// Any-depth: the source names from sources.json are distinctive enough that a directory called
// csharplang/ is upstream content wherever it appears. Reading them from the file means a newly
// added source is covered without editing this script. 'external' is included because that is
// where the submodules lived in dotnet-mcp, and a re-added submodule is the single most likely
// way this repository regains vendored content.
// ---------------------------------------------------------------------------------------------
var rootAnchoredSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "external", "sources", ".sources",
};

var anyDepthSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

var sourcesJson = Path.Combine(repoRoot, "sources.json");
if (File.Exists(sourcesJson))
{
    using var document = JsonDocument.Parse(File.ReadAllText(sourcesJson));
    if (document.RootElement.TryGetProperty("sources", out var sources))
    {
        foreach (var source in sources.EnumerateObject())
            anyDepthSegments.Add(source.Name);
    }
}
else
{
    findings.Add(new Finding("sources-json-missing", "sources.json",
        "sources.json is absent, so the upstream source names could not be derived. "
            + "Rule 2 ran against the root-anchored names only."));
}

var trackedFiles = GitLines(repoRoot, allowExitCode1: false, "ls-files", "-z");

foreach (var file in trackedFiles)
{
    var segments = file.Split('/', StringSplitOptions.RemoveEmptyEntries);

    // Only directory segments count. A file named csharplang-map.md is our own prose about an
    // upstream repo; a file inside a directory named csharplang is that repo's content.
    var directorySegments = segments.Length - 1;

    if (directorySegments > 0 && rootAnchoredSegments.Contains(segments[0]))
    {
        findings.Add(new Finding("upstream-path", file,
            $"Tracked file sits under the repo-root '{segments[0]}/' directory, which is a fetch "
                + "cache location. Upstream sources are fetched, never committed."));
        continue;
    }

    for (var i = 0; i < directorySegments; i++)
    {
        if (anyDepthSegments.Contains(segments[i]))
        {
            findings.Add(new Finding("upstream-path", file,
                $"Tracked file sits under a '{segments[i]}/' directory, which names an upstream "
                    + "source in sources.json. Upstream sources are fetched, never committed."));
            break;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Rule 3 — no tracked file may carry someone else's copyright or license header.
//
// These are the headers the five pinned sources actually ship. A hit is not automatically a
// licensing violation, but it is always something a human has to look at: our own files have no
// reason to carry any of them.
// ---------------------------------------------------------------------------------------------
var headerPatterns = new (string Rule, Regex Pattern)[]
{
    ("dotnet-foundation-header", new Regex(@"Licensed to the \.NET Foundation", RegexOptions.IgnoreCase)),
    ("microsoft-copyright", new Regex(@"Copyright\s*(\(c\)|©)\s*(\d{4}\s*)?Microsoft", RegexOptions.IgnoreCase)),
    ("creative-commons", new Regex(@"Creative Commons|CC[- ]BY(-[A-Z0-9.]+)?\b", RegexOptions.IgnoreCase)),
    ("apache-license", new Regex(@"Apache License,? Version 2\.0", RegexOptions.IgnoreCase)),
};

// ---------------------------------------------------------------------------------------------
// Rule 4 — no tracked file may have the *shape* of upstream material.
//
// Content can be pasted without its header, so the distinctive structure of each source is checked
// too. Each pattern is chosen to be specific enough that our own writing cannot produce it by
// accident: we describe proposals and meetings, we do not reproduce their scaffolding.
// ---------------------------------------------------------------------------------------------
var shapePatterns = new (string Rule, Regex Pattern, string Detail)[]
{
    ("ecma-xml-api-doc",
        new Regex(@"<Type\s+Name=""[^""]+""\s+FullName=""", RegexOptions.IgnoreCase),
        "ECMA-XML API documentation, the file shape used by dotnet-api-docs and roslyn-api-docs."),
    ("csharplang-proposal",
        new Regex(@"^\s*Champion issue:\s*<?https://github\.com/dotnet/(csharplang|vblang)/issues/",
            RegexOptions.IgnoreCase | RegexOptions.Multiline),
        "A csharplang/vblang proposal document's champion-issue header."),
    ("ldm-notes",
        new Regex(@"^#+\s*(C#|Visual Basic) Language Design (Meeting|Notes)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline),
        "Language Design Meeting notes from csharplang or vblang."),
    ("learn-article",
        new Regex(@"^[ \t]*ms\.(author|date|topic):\s*\S", RegexOptions.Multiline),
        "Microsoft Learn article front matter, the file shape used by nuget-docs."),
};

foreach (var file in trackedFiles)
{
    if (contentScanExempt.Contains(file))
        continue;

    var absolute = Path.Combine(repoRoot, file.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(absolute))
        continue; // Staged deletion, or a file removed from disk. Rule 2 already covered its path.

    var text = ReadTextOrNull(absolute);
    if (text is null)
        continue; // Binary. Nothing here reads binaries, and none are tracked today.

    foreach (var (rule, pattern) in headerPatterns)
    {
        if (pattern.IsMatch(text))
        {
            findings.Add(new Finding(rule, file,
                "Carries a third-party copyright or license header. Every file in this repository "
                    + "is authored here and covered by LICENSE; if this content genuinely came from "
                    + "elsewhere, it does not belong in the tree."));
        }
    }

    if (shapeScanExemptExtensions.Contains(Path.GetExtension(file)))
        continue; // Source code cannot *be* an upstream document; see shapeScanExemptExtensions.

    foreach (var (rule, pattern, detail) in shapePatterns)
    {
        if (pattern.IsMatch(text))
            findings.Add(new Finding(rule, file, detail + " Upstream documents are fetched, never committed."));
    }
}

// ---------------------------------------------------------------------------------------------
// Rule 5 (--history) — the same questions, asked of every commit.
//
// Deleting a file in a later commit does not remove it from the repository: a clone still carries
// the blob, and a published history is as much a distribution as a release is. The scans above see
// only the current tree, so they cannot answer "was something committed and then tidied away".
//
// Searching is delegated to `git grep`, which reads packed objects directly and skips binaries with
// -I. The exempt list applies here too, as a pathspec exclusion. It is tempting to scan history
// without it — a hit in a file that is merely exempt today would be worth seeing — but the two
// exempt files exist to *name* these patterns, and that is their purpose in every commit that
// contains them. Without the exclusion this guard reports itself on every run from the commit that
// introduced it onward, which is precisely how a check ends up being ignored or deleted.
// ---------------------------------------------------------------------------------------------
if (scanHistory)
{
    var commits = GitLines(repoRoot, allowExitCode1: false, "rev-list", "--all", "-z");

    if (commits.Count == 0)
    {
        Console.Error.WriteLine("--history: no commits found; nothing to scan.");
    }
    else
    {
        // POSIX ERE for git grep, kept separate from the .NET patterns above rather than shared:
        // the two engines differ in escaping, and a pattern that silently fails to compile here
        // would report a clean history for the wrong reason.
        // IsShape mirrors the split above: a shape rule does not apply to source files, so the
        // history scan excludes them too. Without this the two scans disagree, and a tree that
        // passes reports a finding the moment someone adds --history.
        var historyPatterns = new (string Rule, string Expression, bool IsShape)[]
        {
            ("dotnet-foundation-header", @"Licensed to the \.NET Foundation", false),
            ("microsoft-copyright", @"Copyright ?(\(c\)|\xc2\xa9) ?[0-9]* ?Microsoft", false),
            ("creative-commons", @"Creative Commons|CC-BY", false),
            ("apache-license", @"Apache License,? Version 2\.0", false),
            ("ecma-xml-api-doc", @"<Type Name=""[^""]+"" FullName=""", true),
            ("csharplang-proposal", @"Champion issue: ?<?https://github\.com/dotnet/(csharplang|vblang)/issues/", true),
            ("ldm-notes", @"(C#|Visual Basic) Language Design (Meeting|Notes)", true),
            ("learn-article", @"^[ \t]*ms\.(author|date|topic): ?\S", true),
        };

        // Commit SHAs are passed as arguments, so they are chunked to stay clear of the Windows
        // command-line length limit. Every commit is scanned; nothing is sampled or capped.
        const int commitsPerInvocation = 400;

        foreach (var (rule, expression, isShape) in historyPatterns)
        {
            for (var offset = 0; offset < commits.Count; offset += commitsPerInvocation)
            {
                var chunk = commits.Skip(offset).Take(commitsPerInvocation).ToArray();

                var arguments = new List<string> { "grep", "-I", "-l", "-E", expression };
                arguments.AddRange(chunk);

                // Everything after `--` is a pathspec; ':!<path>' excludes it from the search.
                arguments.Add("--");
                foreach (var exempt in contentScanExempt)
                    arguments.Add($":!{exempt}");

                if (isShape)
                {
                    foreach (var extension in shapeScanExemptExtensions)
                        arguments.Add($":!*{extension}");
                }

                // git grep exits 1 when it matches nothing, which is the common case here.
                foreach (var hit in GitLines(repoRoot, allowExitCode1: true, [.. arguments]))
                {
                    // Output is "<commit>:<path>".
                    var separator = hit.IndexOf(':');
                    var commit = separator < 0 ? "?" : hit[..separator];
                    var path = separator < 0 ? hit : hit[(separator + 1)..];

                    findings.Add(new Finding($"history/{rule}", path,
                        $"Present in commit {(commit.Length >= 8 ? commit[..8] : commit)}. A clone still "
                            + "carries this blob even if the file is gone from the current tree. "
                            + "Removing it means rewriting history, not deleting the file."));
                }
            }
        }

        // Paths are checked separately: a directory can be committed and later deleted without any
        // of its files matching a content pattern.
        var historicalPaths = GitLines(repoRoot, allowExitCode1: false, "log", "--all", "--pretty=format:", "--name-only", "--diff-filter=A");

        foreach (var path in historicalPaths.Distinct(StringComparer.Ordinal))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var directorySegments = segments.Length - 1;
            if (directorySegments <= 0)
                continue;

            if (rootAnchoredSegments.Contains(segments[0]))
            {
                findings.Add(new Finding("history/upstream-path", path,
                    $"Existed in history under the repo-root '{segments[0]}/' fetch-cache directory."));
                continue;
            }

            for (var i = 0; i < directorySegments; i++)
            {
                if (anyDepthSegments.Contains(segments[i]))
                {
                    findings.Add(new Finding("history/upstream-path", path,
                        $"Existed in history under a '{segments[i]}/' directory, which names an "
                            + "upstream source in sources.json."));
                    break;
                }
            }
        }

        Console.Error.WriteLine($"--history: scanned {commits.Count} commit(s) and "
            + $"{historicalPaths.Distinct(StringComparer.Ordinal).Count()} distinct historical path(s).");
    }
}

// ---------------------------------------------------------------------------------------------
// Report. No silent truncation: every finding is printed.
// ---------------------------------------------------------------------------------------------
if (emitJson)
{
    var payload = new
    {
        ok = findings.Count == 0,
        checkedFiles = trackedFiles.Count,
        findings = findings.Select(f => new { rule = f.Rule, path = f.Path, detail = f.Detail }),
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    return findings.Count == 0 ? 0 : 1;
}

if (findings.Count == 0)
{
    Console.WriteLine($"OK — {trackedFiles.Count} tracked files, no vendored upstream content.");
    return 0;
}

Console.Error.WriteLine($"FAIL — {findings.Count} finding(s) across {trackedFiles.Count} tracked files.");
Console.Error.WriteLine();
foreach (var group in findings.GroupBy(f => f.Rule))
{
    Console.Error.WriteLine($"[{group.Key}]");
    foreach (var finding in group)
    {
        Console.Error.WriteLine($"  {finding.Path}");
        Console.Error.WriteLine($"      {finding.Detail}");
    }
    Console.Error.WriteLine();
}
Console.Error.WriteLine("See THIRD-PARTY-NOTICES.md. Upstream content is fetched into a per-user");
Console.Error.WriteLine("cache by sync_source; it is never committed to this repository.");
return 1;

// ---------------------------------------------------------------------------------------------

static bool IsIgnored(string repoRoot, string relativePath)
{
    // check-ignore exits 0 when the path is ignored, 1 when it is not, and >1 on error. The path
    // need not exist, which is the point: this asks about the rule, not about the file.
    var psi = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false };
    psi.ArgumentList.Add("check-ignore");
    psi.ArgumentList.Add("-q");
    psi.ArgumentList.Add(relativePath);
    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
    process.WaitForExit();
    return process.ExitCode == 0;
}

// allowExitCode1 exists for `git grep`, which reports "no match" as exit 1. Treating that as a
// failure would turn the clean case into a crash; treating every non-zero exit as "no match" would
// hide real errors. Only the callers that expect it pass true. It is a required parameter rather
// than an overload because top-level-program local functions cannot be overloaded.
static List<string> GitLines(string repoRoot, bool allowExitCode1, params string[] arguments)
{
    var psi = new ProcessStartInfo("git")
    {
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };
    foreach (var argument in arguments)
        psi.ArgumentList.Add(argument);

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0 && !(allowExitCode1 && process.ExitCode == 1))
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited with {process.ExitCode}.");

    // -z output is NUL-separated; everything else is newline-separated. Splitting on both keeps one
    // helper usable for each, and empty entries are dropped either way.
    return [.. output.Split(['\0', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)];
}

static string? ReadTextOrNull(string path)
{
    var bytes = File.ReadAllBytes(path);

    // A NUL byte in the first block is the usual binary tell, and matches how git decides.
    var probeLength = Math.Min(bytes.Length, 8000);
    for (var i = 0; i < probeLength; i++)
    {
        if (bytes[i] == 0)
            return null;
    }

    return Encoding.UTF8.GetString(bytes);
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null)
    {
        // In a linked worktree `.git` is a FILE holding a gitdir pointer, not a directory. Testing
        // only for a directory walks straight past the worktree root to the main checkout, so the
        // scan reports on code the caller is not working in — a pass that measured nothing.
        var gitPath = Path.Combine(dir.FullName, ".git");
        if (Directory.Exists(gitPath) || File.Exists(gitPath))
            return dir.FullName;
        dir = dir.Parent;
    }
    return null;
}

internal readonly record struct Finding(string Rule, string Path, string Detail);
