#!/usr/bin/env dotnet
#:property PublishAot=false

// Installs the exact SDK bands used by the corpus test matrix into a repository-private directory.
// The private host is intentionally never added to PATH and does not alter any machine installation.

using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

#nullable enable

var requiredVersions = new[] { "5.0.408", "7.0.410", "10.0.302" };
var installDir = "";
var checkOnly = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            checkOnly = true;
            break;
        case "--install-dir":
            if (installDir.Length != 0 || ++i >= args.Length || args[i].Length == 0
                || args[i].StartsWith("--", StringComparison.Ordinal))
                return Usage("--install-dir requires one path and can be specified only once.");

            installDir = args[i];
            break;
        default:
            return Usage($"Unknown option: {args[i]}");
    }
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("install-corpus-test-sdks.cs supports Windows only; the corpus matrix CI is Windows.");
    return 1;
}

var repoRoot = FindRepoRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("Could not locate the repository root (no .git entry found).");
var resolvedInstallDir = Path.GetFullPath(installDir.Length == 0
    ? Path.Combine(repoRoot, ".artifacts", "dotnet")
    : installDir);
var privateHost = Path.Combine(resolvedInstallDir, "dotnet.exe");

Console.WriteLine($"Private SDK directory: {resolvedInstallDir}");

var presentVersions = ListInstalledSdkVersions(privateHost);
var missingVersions = requiredVersions.Where(version => !presentVersions.Contains(version)).ToArray();

foreach (var version in requiredVersions)
    Console.WriteLine(presentVersions.Contains(version) ? $"Found SDK: {version}" : $"Missing SDK: {version}");

if (checkOnly)
{
    if (missingVersions.Length == 0)
    {
        Console.WriteLine("All required private SDKs are present.");
        return 0;
    }

    Console.Error.WriteLine($"Missing {missingVersions.Length} required private SDK version(s).");
    return 1;
}

if (missingVersions.Length != 0)
{
    Directory.CreateDirectory(resolvedInstallDir);
    var installerPath = Path.Combine(Path.GetTempPath(), $"dotnet-install-{Guid.NewGuid():N}.ps1");

    try
    {
        Console.WriteLine("Downloading the Microsoft dotnet-install script to a temporary file.");
        using var client = new HttpClient();
        await File.WriteAllBytesAsync(installerPath,
            await client.GetByteArrayAsync("https://dot.net/v1/dotnet-install.ps1"));

        foreach (var version in missingVersions)
        {
            Console.WriteLine($"Installing SDK {version} into the private directory.");
            var result = await RunProcess(
                "powershell.exe",
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installerPath,
                "-Version", version, "-InstallDir", resolvedInstallDir, "-NoPath");
            ReportProcessOutput($"Installer output for SDK {version}", result);
            RequireSuccess($"Installer for SDK {version}", result);
        }
    }
    finally
    {
        if (File.Exists(installerPath))
            File.Delete(installerPath);
    }
}

presentVersions = ListInstalledSdkVersions(privateHost);
missingVersions = requiredVersions.Where(version => !presentVersions.Contains(version)).ToArray();
if (missingVersions.Length != 0)
    throw new InvalidOperationException("The private installation completed without all required SDKs: "
        + string.Join(", ", missingVersions) + ".");

Console.WriteLine("All required private SDKs are present.");
Console.WriteLine($"Private test host: {QuoteForDisplay(privateHost)} test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj --nologo");
return 0;

static int Usage(string error)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("Usage: dotnet scripts/install-corpus-test-sdks.cs -- [--check] [--install-dir <path>]");
    return 2;
}

static HashSet<string> ListInstalledSdkVersions(string privateHost)
{
    if (!File.Exists(privateHost))
        return new HashSet<string>(StringComparer.Ordinal);

    var result = RunProcess(privateHost, "--list-sdks").GetAwaiter().GetResult();
    ReportProcessOutput("Private dotnet --list-sdks output", result);
    RequireSuccess("Private dotnet --list-sdks", result);

    var versions = new HashSet<string>(StringComparer.Ordinal);
    foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
    {
        var match = Regex.Match(line, @"^(?<version>\d+\.\d+\.\d+)\s+\[");
        if (match.Success)
            versions.Add(match.Groups["version"].Value);
    }

    return versions;
}

static async Task<ProcessResult> RunProcess(string fileName, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
    var standardOutput = process.StandardOutput.ReadToEndAsync();
    var standardError = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await Task.WhenAll(standardOutput, standardError);
    return new ProcessResult(process.ExitCode, standardOutput.Result, standardError.Result);
}

static void ReportProcessOutput(string heading, ProcessResult result)
{
    if (result.StandardOutput.Length != 0)
        Console.WriteLine($"{heading} (stdout):{Environment.NewLine}{result.StandardOutput.TrimEnd()}");
    if (result.StandardError.Length != 0)
        Console.Error.WriteLine($"{heading} (stderr):{Environment.NewLine}{result.StandardError.TrimEnd()}");
}

static void RequireSuccess(string operation, ProcessResult result)
{
    if (result.ExitCode == 0)
        return;

    throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}. "
        + "Its captured stdout and stderr were reported above.");
}

static string? FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        var gitEntry = Path.Combine(directory.FullName, ".git");
        if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
            return directory.FullName;
        directory = directory.Parent;
    }

    return null;
}

static string QuoteForDisplay(string path) => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
