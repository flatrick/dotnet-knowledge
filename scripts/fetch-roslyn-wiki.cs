#!/usr/bin/env dotnet
#:property PublishAot=false

// Fetches docs/wiki from the dotnet/roslyn repo at a pinned commit into
// external/roslyn-wiki/. No submodule, no persistent clone — the temp
// working tree is deleted after the copy.
//
// Run from repo root:
//   dotnet scripts/fetch-roslyn-wiki.cs
//   dotnet scripts/fetch-roslyn-wiki.cs -- --sha <commit-sha>
//
// To update the pin, change PinnedSha below and re-run.

using System.Diagnostics;

const string PinnedSha = "fca0128889ed81ab7a8754586be0ed18aadb4b14";
const string RepoUrl = "https://github.com/dotnet/roslyn.git";
const string SparsePath = "docs/wiki";

var sha = Environment.GetEnvironmentVariable("ROSLYN_SHA") ?? PinnedSha;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--sha" && i + 1 < args.Length)
        sha = args[++i];
    else
    {
        Console.Error.WriteLine($"Unknown argument: {args[i]}");
        return 1;
    }
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Could not locate repo root (no .git directory found).");

var destDir = Path.Combine(repoRoot, "external", "roslyn-wiki");
var tempDir = Path.Combine(Path.GetTempPath(), $"roslyn-wiki-{Guid.NewGuid():N}");

try
{
    Directory.CreateDirectory(tempDir);

    Console.WriteLine("==> Cloning roslyn (blobless sparse) into temp dir...");
    Git(null, "clone", "--filter=blob:none", "--no-checkout", "--sparse", "--quiet", RepoUrl,
        Path.Combine(tempDir, "roslyn"));

    var roslynDir = Path.Combine(tempDir, "roslyn");

    Console.WriteLine($"==> Configuring sparse checkout for {SparsePath}...");
    Git(roslynDir, "sparse-checkout", "set", SparsePath);

    Console.WriteLine($"==> Fetching commit {sha}...");
    Git(roslynDir, "fetch", "--depth", "1", "--quiet", "origin", sha);

    Console.WriteLine("==> Checking out...");
    Git(roslynDir, "checkout", "--quiet", sha);

    var sourcePath = Path.Combine(roslynDir, SparsePath);
    if (!Directory.Exists(sourcePath))
    {
        Console.Error.WriteLine($"ERROR: {SparsePath} not found in repo at {sha}");
        return 1;
    }

    Console.WriteLine($"==> Copying to {destDir}...");
    if (Directory.Exists(destDir))
        Directory.Delete(destDir, recursive: true);
    Directory.CreateDirectory(destDir);
    CopyDirectory(sourcePath, destDir);

    File.WriteAllText(Path.Combine(destDir, ".roslyn-commit"), sha);

    Console.WriteLine();
    Console.WriteLine($"Done. {destDir} populated from dotnet/roslyn @ {sha}");
    Console.WriteLine("Commit that file to lock the pin, or add external/roslyn-wiki to .gitignore to keep it fetch-only.");
    return 0;
}
finally
{
    if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, recursive: true);
}

static void Git(string? workingDir, params string[] arguments)
{
    var psi = new ProcessStartInfo("git") { UseShellExecute = false };
    if (workingDir is not null)
        psi.WorkingDirectory = workingDir;
    foreach (var arg in arguments)
        psi.ArgumentList.Add(arg);
    using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
    p.WaitForExit();
    if (p.ExitCode != 0)
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited with {p.ExitCode}.");
}

static void CopyDirectory(string source, string dest)
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, file);
        var target = Path.Combine(dest, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

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
