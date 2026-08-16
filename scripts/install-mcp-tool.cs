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
//   dotnet scripts/install-mcp-tool.cs -- reinstall    # stop running instances, then install
//   dotnet scripts/install-mcp-tool.cs -- uninstall    # remove the user-global tool
//   dotnet scripts/install-mcp-tool.cs -- status       # explicit form of the default
//
// `reinstall` exists because Windows holds an exclusive lock on the shim executable while the
// server runs, so `install` against a live server fails on the tool uninstall/install step. Unix
// replaces a running executable's file happily, so there the stop step only serves to retire a
// server still answering with the old build.
//
// Self-location: the checkout root is the parent of this script's own `scripts/` directory,
// resolved via [CallerFilePath] — never a CWD-based repo-root walk. Running a worktree's copy
// of this script always builds/installs *that worktree's* code, regardless of the shell's CWD.
//
// The install is machine-global, so one worktree's install replaces every session's server. The
// packed version carries the commit it was built from (Nerdbank.GitVersioning, version.json), and
// `status` compares it against this checkout's HEAD so the swap is visible rather than silent.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

const string PackageId = "flatrick.DotNetKnowledge";
const string ToolCommandName = "dotnet-knowledge";
const int StopTimeoutMilliseconds = 10_000;

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
    ("reinstall", []) => RunReinstall(checkoutRoot),
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
    Console.WriteLine("  dotnet scripts/install-mcp-tool.cs -- reinstall   stop running instances, then install");
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

static string GetToolsDir() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");

static string GetShimPath() =>
    Path.Combine(GetToolsDir(), ToolCommandName + (OperatingSystem.IsWindows() ? ".exe" : ""));

static void PrintPathHealthCheck()
{
    var toolsDir = GetToolsDir();
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

    var shimPath = GetShimPath();
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

static int RunReinstall(string checkoutRoot)
{
    var stopExit = StopRunningInstances();
    return stopExit != 0 ? stopExit : RunInstall(checkoutRoot);
}

static int StopRunningInstances()
{
    var shimPath = GetShimPath();

    List<(int Pid, string ExePath)> running;
    try
    {
        running = FindShimProcesses(shimPath);
    }
    catch (Exception ex)
    {
        return ReportStopFailure($"Could not enumerate running processes: {ex.Message}");
    }

    if (running.Count == 0)
    {
        Console.WriteLine($"No running {ToolCommandName} instances.");
        return 0;
    }

    var failed = false;
    foreach (var (pid, _) in running)
    {
        Console.WriteLine($"Stopping {ToolCommandName} (PID {pid})...");
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: false);

            if (process.WaitForExit(StopTimeoutMilliseconds))
            {
                Console.WriteLine("  stopped.");
            }
            else
            {
                Console.Error.WriteLine($"  PID {pid} did not exit within {StopTimeoutMilliseconds / 1000} seconds.");
                failed = true;
            }
        }
        catch (ArgumentException)
        {
            // Exited between the scan and the kill, which is the outcome we wanted anyway.
            Console.WriteLine("  already exited.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"  could not stop PID {pid}: {ex.Message}");
            failed = true;
        }
    }

    return failed ? ReportStopFailure($"One or more {ToolCommandName} instances are still running.") : 0;
}

// Whether an unstoppable instance blocks the install is a platform question, not a policy choice:
// Windows holds an exclusive lock on the shim while it runs, so continuing would only reach the
// same failure later wearing an opaque file-lock message. Unix lets the install replace the file
// underneath a running process, so there the same situation costs nothing but a stale server.
static int ReportStopFailure(string message)
{
    if (OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine($"  Windows locks {GetShimPath()} while it runs, so the install cannot replace it.");
        return 1;
    }

    Console.WriteLine($"warning: {message}");
    Console.WriteLine("  Continuing: this platform can replace a running executable's file, so the install still applies.");
    Console.WriteLine("  Any instance left running keeps serving its old build until it is restarted.");
    return 0;
}

// Identifying another process's executable is where the three platforms diverge most, so each gets
// the mechanism native to it rather than one mechanism wrapped in rescue paths.
static List<(int Pid, string ExePath)> FindShimProcesses(string shimPath)
{
    if (OperatingSystem.IsWindows())
        return FindShimProcessesViaMainModule(shimPath);

    return OperatingSystem.IsLinux()
        ? FindShimProcessesViaProc(shimPath)
        : FindShimProcessesViaPs(shimPath);
}

static List<(int Pid, string ExePath)> FindShimProcessesViaMainModule(string shimPath)
{
    var found = new List<(int, string)>();

    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(shimPath)))
    {
        using (process)
        {
            string? exePath;
            try
            {
                exePath = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Owned by another user, elevated, or gone since the enumeration. A process whose
                // executable cannot be read is never assumed to be ours.
                Console.WriteLine($"note: could not read the executable path of PID {process.Id}; skipping it.");
                continue;
            }

            if (exePath is not null && PathsEqual(exePath, shimPath))
                found.Add((process.Id, exePath));
        }
    }

    return found;
}

// Deliberately not a name lookup: Linux truncates a process name to 15 characters, and
// `dotnet-knowledge` is 16. A name match would therefore find nothing on Linux while working on
// Windows, with no error to show for it. The `exe` link carries the full path and cannot be
// truncated.
static List<(int Pid, string ExePath)> FindShimProcessesViaProc(string shimPath)
{
    var found = new List<(int, string)>();

    foreach (var dir in Directory.EnumerateDirectories("/proc"))
    {
        if (!int.TryParse(Path.GetFileName(dir), out var pid))
            continue;

        string? exePath;
        try
        {
            exePath = File.ResolveLinkTarget(Path.Combine(dir, "exe"), returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another user's process, or one that exited mid-scan. Neither is ours.
            continue;
        }

        if (exePath is not null && PathsEqual(exePath, shimPath))
            found.Add((pid, exePath));
    }

    return found;
}

// macOS restricts reading another process's modules, so MainModule is not available here. `ps` is
// started through ArgumentList with UseShellExecute = false, exactly as git and dotnet are above —
// a process, not a shell.
static List<(int Pid, string ExePath)> FindShimProcessesViaPs(string shimPath)
{
    var found = new List<(int, string)>();

    var (exitCode, stdout) = RunProcessCapture("ps", ["-Ao", "pid=,comm="]);
    if (exitCode != 0)
        throw new InvalidOperationException($"'ps -Ao pid=,comm=' failed (exit {exitCode}).");

    foreach (var line in stdout.Split('\n'))
    {
        var trimmed = line.Trim();
        var separator = trimmed.IndexOf(' ');
        if (separator < 0 || !int.TryParse(trimmed[..separator], out var pid))
            continue;

        // An executable path may contain spaces, so everything after the first gap is the path.
        var exePath = trimmed[(separator + 1)..].TrimStart();
        if (MatchesShim(exePath, shimPath))
            found.Add((pid, exePath));
    }

    return found;
}

// The `comm` column carries the executable's path on macOS. Should it arrive as a bare command
// name instead, match that rather than reporting nothing — a bare name has no directory to
// disambiguate, so this is the weaker comparison and is only reached when the stronger one cannot
// apply.
static bool MatchesShim(string candidate, string shimPath) =>
    PathsEqual(candidate, shimPath)
    || (!candidate.Contains('/') && string.Equals(candidate, Path.GetFileName(shimPath), StringComparison.Ordinal));

static bool PathsEqual(string left, string right)
{
    try
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
        return false;
    }
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
