#!/usr/bin/env dotnet
#:property PublishAot=false
#nullable enable

// Build, install, and manage the MCP server as a user-global .NET tool. This is how the server is
// meant to be launched: `dotnet run --project src/DotNetKnowledge.Mcp` rebuilds on every client
// start and pins the client config to one checkout path, whereas the installed tool is a single
// `dotnet-knowledge` command that any client on the machine can launch.
//
// Run from repo root (or any directory — the script self-locates from its own file path):
//   dotnet scripts/install-mcp-tool.cs                 # status + usage hints (default)
//   dotnet scripts/install-mcp-tool.cs -- install      # pack + install/update the user-global tool
//   dotnet scripts/install-mcp-tool.cs -- uninstall    # remove the user-global tool
//   dotnet scripts/install-mcp-tool.cs -- status       # explicit form of the default
//
// Self-location: the checkout root is the parent of this script's own `scripts/` directory,
// resolved via [CallerFilePath] — never a CWD-based repo-root walk. Running a worktree's copy
// of this script always builds/installs *that worktree's* code, regardless of the shell's CWD.
//
// The install is machine-global, so one worktree's install replaces every session's server. The
// packed version carries the commit it was built from (Nerdbank.GitVersioning, version.json), and
// `status` compares it against this checkout's HEAD so the swap is visible rather than silent.

using System.Diagnostics;
using System.Text.RegularExpressions;

const string PackageId = "flatrick.DotNetKnowledge";
const string ToolCommandName = "dotnet-knowledge";

var checkoutRoot = FindCheckoutRoot();
var gitDir = Path.Combine(checkoutRoot, ".git");
if (!Directory.Exists(gitDir) && !File.Exists(gitDir))
{
    Console.Error.WriteLine($"Not a git checkout: {checkoutRoot} has no .git directory or file.");
    return 1;
}

var command = args.Length > 0 ? args[0] : "status";
var commandArgs = args.Skip(1).ToArray();

return (command, commandArgs) switch
{
    ("status", []) => RunStatus(checkoutRoot),
    ("install", []) => RunInstall(checkoutRoot),
    ("uninstall", []) => RunUninstall(),
    _ => UnknownCommand(command, commandArgs)
};

static int UnknownCommand(string command, string[] commandArgs)
{
    var full = string.Join(' ', new[] { command }.Concat(commandArgs));
    Console.Error.WriteLine($"Unknown command: {full}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet scripts/install-mcp-tool.cs                status (default)");
    Console.WriteLine("  dotnet scripts/install-mcp-tool.cs -- install     install/update the user-global tool");
    Console.WriteLine("  dotnet scripts/install-mcp-tool.cs -- uninstall   remove the user-global tool");
    Console.WriteLine("  dotnet scripts/install-mcp-tool.cs -- status      explicit form of the default");
}

static int RunStatus(string checkoutRoot)
{
    var globalVersion = GetInstalledVersion();

    Console.WriteLine(globalVersion is null
        ? "Global tool: not installed."
        : $"Global tool: {globalVersion} ({DescribeCommitMatch(checkoutRoot, globalVersion)})");

    var branch = GetCurrentBranch(checkoutRoot);
    Console.WriteLine($"This checkout: branch {branch ?? "(detached HEAD)"}.");

    if (IsDirty(checkoutRoot))
        Console.WriteLine("note: checkout has uncommitted changes.");

    Console.WriteLine();
    Console.WriteLine("This describes the INSTALLED tool — what would launch next. A client that is");
    Console.WriteLine("already connected keeps serving the build it started, so restart the MCP");
    Console.WriteLine("connection after an install for the two to agree.");

    PrintPathHealthCheck();
    PrintUsage();
    return 0;
}

static string? GetCurrentBranch(string checkoutRoot)
{
    var (exitCode, stdout) = RunGitCapture(checkoutRoot, "rev-parse", "--abbrev-ref", "HEAD");
    if (exitCode != 0)
        return null;

    var branch = stdout.Trim();
    return branch is "" or "HEAD" ? null : branch;
}

static string DescribeCommitMatch(string checkoutRoot, string installedVersion)
{
    var installedSha = ExtractShaFromVersion(installedVersion);
    if (installedSha is null)
        return "version has no embedded commit id";

    // Ask git for HEAD abbreviated to the SAME length as the embedded sha — NB.GV's default
    // abbreviation length need not match git's, so comparing at mismatched lengths would give
    // false negatives.
    var (exitCode, stdout) = RunGitCapture(checkoutRoot, "rev-parse", $"--short={installedSha.Length}", "HEAD");
    if (exitCode != 0)
        return "could not determine HEAD commit";

    var headSha = stdout.Trim();
    return installedSha.Equals(headSha, StringComparison.OrdinalIgnoreCase)
        ? "up to date with this checkout's HEAD"
        : $"installed from a different commit; this checkout's HEAD is {headSha}";
}

static string? ExtractShaFromVersion(string version)
{
    var match = Regex.Match(version, @"g([0-9a-f]{7,40})$");
    return match.Success ? match.Groups[1].Value : null;
}

static bool IsDirty(string checkoutRoot)
{
    var (exitCode, stdout) = RunGitCapture(checkoutRoot, "status", "--porcelain");
    return exitCode == 0 && stdout.Trim().Length > 0;
}

static bool IsToolsDirOnPath(string toolsDir)
{
    var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    return pathVar.Split(Path.PathSeparator).Any(p => string.Equals(p.Trim(), toolsDir, comparison));
}

static void PrintPathHealthCheck()
{
    var toolsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
    if (IsToolsDirOnPath(toolsDir))
        return;

    Console.WriteLine($"warning: {toolsDir} is not on PATH; the `{ToolCommandName}` command will not be found.");
    Console.WriteLine(OperatingSystem.IsWindows()
        ? $"  The .NET SDK installer normally adds this automatically. If not: setx PATH \"%PATH%;{toolsDir}\" (then re-open your terminal)"
        : $"  Add this to your shell profile (~/.bashrc, ~/.zshrc, etc.): export PATH=\"$PATH:{toolsDir}\"");
}

static string? ParseInstalledVersion(string toolListOutput)
{
    foreach (var line in toolListOutput.Split('\n'))
    {
        var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length >= 2 && columns[0].Equals(PackageId, StringComparison.OrdinalIgnoreCase))
            return columns[1];
    }
    return null;
}

static string? GetInstalledVersion()
{
    var (exitCode, stdout) = RunProcessCapture("dotnet", ["tool", "list", "--global"]);
    return exitCode == 0 ? ParseInstalledVersion(stdout) : null;
}

static (int ExitCode, string StdOut) RunGitCapture(string checkoutRoot, params string[] gitArgs)
{
    var psi = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true };
    psi.ArgumentList.Add("-C");
    psi.ArgumentList.Add(checkoutRoot);
    foreach (var a in gitArgs)
        psi.ArgumentList.Add(a);

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout);
}

static (int ExitCode, string StdOut) RunProcessCapture(string executable, IEnumerable<string> arguments)
{
    var psi = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (var arg in arguments)
        psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout);
}

static string FindCheckoutRoot([System.Runtime.CompilerServices.CallerFilePath] string scriptPath = "")
{
    var scriptsDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath))
        ?? throw new InvalidOperationException($"Could not determine directory of script path: {scriptPath}");
    var root = Path.GetDirectoryName(scriptsDir)
        ?? throw new InvalidOperationException($"Could not determine checkout root above: {scriptsDir}");
    return root;
}

static int RunInstall(string checkoutRoot)
{
    if (IsDirty(checkoutRoot))
        Console.WriteLine("warning: checkout has uncommitted changes; the packed version is derived from HEAD and will not reflect them.");

    var packDir = Path.Combine(checkoutRoot, ".artifacts", "nuget-local");
    Directory.CreateDirectory(packDir);
    foreach (var stale in Directory.GetFiles(packDir, "*.nupkg"))
        File.Delete(stale);

    var serverProject = Path.Combine(checkoutRoot, "src", "DotNetKnowledge.Mcp");
    var packExit = RunProcess("dotnet", ["pack", serverProject, "-c", "Release", "-o", packDir]);
    if (packExit != 0)
    {
        Console.Error.WriteLine($"pack failed (exit {packExit}).");
        return packExit;
    }

    var targetVersion = GetVersionFromNupkg(packDir);
    Console.WriteLine($"Packed version: {targetVersion}");

    var (localsExit, localsOut) = RunProcessCapture("dotnet", ["nuget", "locals", "global-packages", "--list"]);
    if (localsExit != 0)
    {
        Console.Error.WriteLine($"'dotnet nuget locals global-packages --list' failed (exit {localsExit}).");
        return localsExit;
    }

    // A reinstall of an unchanged HEAD reuses the same version string, and NuGet would then serve
    // the previously extracted package rather than the one just packed. Evicting the cached entry
    // is what makes `-- install` mean "install what is on disk now".
    var globalPackagesRoot = ParseNugetLocalsOutput(localsOut);
    var cachedVersionDir = Path.Combine(globalPackagesRoot, PackageId.ToLowerInvariant(), targetVersion.ToLowerInvariant());
    if (Directory.Exists(cachedVersionDir))
    {
        Directory.Delete(cachedVersionDir, recursive: true);
        Console.WriteLine($"Evicted stale cache entry: {cachedVersionDir}");
    }

    var installedVersion = GetInstalledVersion();
    int installExit;

    if (installedVersion is null || !installedVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
    {
        var updateArgs = new List<string> { "tool", "update", "--global", PackageId, "--add-source", packDir, "--version", targetVersion };
        installExit = RunProcess("dotnet", updateArgs);
    }
    else
    {
        var uninstallArgs = new List<string> { "tool", "uninstall", "--global", PackageId };
        var uninstallExit = RunProcess("dotnet", uninstallArgs);

        if (uninstallExit != 0)
        {
            installExit = uninstallExit;
        }
        else
        {
            var installArgs = new List<string> { "tool", "install", "--global", PackageId, "--add-source", packDir, "--version", targetVersion };
            installExit = RunProcess("dotnet", installArgs);
        }
    }

    if (installExit != 0)
    {
        Console.Error.WriteLine($"Install failed (exit {installExit}).");
        return installExit;
    }

    PrintPathHealthCheck();

    var shimName = ToolCommandName + (OperatingSystem.IsWindows() ? ".exe" : "");
    var shimDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
    var shimPath = Path.Combine(shimDir, shimName);
    if (!File.Exists(shimPath))
    {
        Console.Error.WriteLine($"Install reported success but shim not found at: {shimPath}");
        return 1;
    }

    var (headExit, headOut) = RunGitCapture(checkoutRoot, "rev-parse", "--short", "HEAD");
    var branch = GetCurrentBranch(checkoutRoot);
    Console.WriteLine($"Installed {PackageId} {targetVersion} (commit {(headExit == 0 ? headOut.Trim() : "unknown")}, branch {branch ?? "detached HEAD"}).");
    Console.WriteLine($"Launch: {ToolCommandName}");
    Console.WriteLine("Restart the MCP connection for a running client to pick this up.");
    return 0;
}

static int RunUninstall()
{
    var installedVersion = GetInstalledVersion();
    if (installedVersion is null)
    {
        Console.WriteLine($"{PackageId} is not installed as a global tool. Nothing to do.");
        return 0;
    }

    var exitCode = RunProcess("dotnet", ["tool", "uninstall", "--global", PackageId]);
    if (exitCode != 0)
    {
        Console.Error.WriteLine($"Uninstall failed (exit {exitCode}).");
        return exitCode;
    }

    Console.WriteLine($"Uninstalled {PackageId} {installedVersion}.");
    return 0;
}

static string GetVersionFromNupkg(string packDir)
{
    var nupkgFiles = Directory.GetFiles(packDir, "*.nupkg");
    if (nupkgFiles.Length != 1)
        throw new InvalidOperationException($"Expected exactly one .nupkg in {packDir}, found {nupkgFiles.Length}.");

    var fileName = Path.GetFileName(nupkgFiles[0]);
    var match = Regex.Match(fileName, $@"^{Regex.Escape(PackageId)}\.(.+)\.nupkg$", RegexOptions.IgnoreCase);
    if (!match.Success)
        throw new InvalidOperationException($"Could not parse version from nupkg filename: {fileName}");
    return match.Groups[1].Value;
}

static string ParseNugetLocalsOutput(string output)
{
    var match = Regex.Match(output, @"global-packages:\s*(.+)", RegexOptions.IgnoreCase);
    if (!match.Success)
        throw new InvalidOperationException($"Could not parse 'dotnet nuget locals' output: {output}");
    return match.Groups[1].Value.Trim();
}

static int RunProcess(string executable, IEnumerable<string> arguments, string? workingDirectory = null)
{
    var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
    if (workingDirectory is not null)
        psi.WorkingDirectory = workingDirectory;
    foreach (var arg in arguments)
        psi.ArgumentList.Add(arg);

    Console.WriteLine($"Running: {executable} {string.Join(' ', arguments)}");
    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {executable}.");
    process.WaitForExit();
    return process.ExitCode;
}
#nullable restore
