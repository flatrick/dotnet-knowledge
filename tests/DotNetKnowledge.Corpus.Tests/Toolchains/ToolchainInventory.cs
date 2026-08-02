using System.Text.RegularExpressions;
using DotNetKnowledge.Corpus.Tests.Execution;

namespace DotNetKnowledge.Corpus.Tests.Toolchains;

internal sealed partial class ToolchainInventory
{
    private static readonly Lazy<Task<ToolchainInventory>> CurrentDiscovery = new(
        () => DiscoverCurrent(new ProcessRunner()));

    private readonly IReadOnlyList<InstalledSdk> installedSdks;
    private readonly IReadOnlyList<InstalledRuntime> installedRuntimes;

    private ToolchainInventory(IReadOnlyList<InstalledSdk> installedSdks, IReadOnlyList<InstalledRuntime> installedRuntimes)
    {
        this.installedSdks = installedSdks;
        this.installedRuntimes = installedRuntimes;
    }

    public static async Task<ToolchainInventory> Discover(string dotnetPath, ProcessRunner runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetPath);
        ArgumentNullException.ThrowIfNull(runner);

        var sdkResultTask = runner.RunAsync(dotnetPath, ["--list-sdks"]);
        var runtimeResultTask = runner.RunAsync(dotnetPath, ["--list-runtimes"]);
        await Task.WhenAll(sdkResultTask, runtimeResultTask);

        var sdkResult = await sdkResultTask;
        var runtimeResult = await runtimeResultTask;
        EnsureSucceeded(sdkResult);
        EnsureSucceeded(runtimeResult);
        return FromListings(sdkResult.StandardOutput, runtimeResult.StandardOutput);
    }

    public static Task<ToolchainInventory> DiscoverCurrent(ProcessRunner runner)
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return Discover(string.IsNullOrWhiteSpace(hostPath) ? "dotnet" : hostPath, runner);
    }

    public static Task<ToolchainInventory> Current => CurrentDiscovery.Value;

    internal static ToolchainInventory FromListings(string sdkListing, string runtimeListing)
    {
        ArgumentNullException.ThrowIfNull(sdkListing);
        ArgumentNullException.ThrowIfNull(runtimeListing);

        return new ToolchainInventory(ParseSdks(sdkListing), ParseRuntimes(runtimeListing));
    }

    public InstalledSdk ResolveSdk(string band)
    {
        var requiredVersion = RequiredSdkVersion(band);
        
        var candidate = installedSdks
            .FirstOrDefault(sdk => sdk.Version == requiredVersion);
        
        if (candidate is not null)
        {
            return candidate;
        }

        var installedVersions = installedSdks
            .OrderBy(sdk => sdk.Version)
            .Select(sdk => sdk.Version.ToString())
            .ToArray();
        var installedVersionList = installedVersions.Length == 0
            ? "(none)"
            : string.Join(", ", installedVersions);
        throw new InvalidOperationException(
            $"Required .NET SDK {requiredVersion} for band {band} is not installed. " +
            $"Installed SDKs: {installedVersionList}.");
    }

    public static Version RequiredSdkVersion(string band)
    {
        var parsedBand = ParseBand(band);
        return parsedBand switch
        {
            (5, 0) => new Version(5, 0, 408),
            (7, 0) => new Version(7, 0, 410),
            (10, 0) => new Version(10, 0, 302),
            _ => throw new InvalidOperationException(
                $"SDK band {band} has no configured exact version. " +
                "Configured SDKs: 5.0.408, 7.0.410, 10.0.302.")
        };
    }

    public bool HasRuntime(string majorMinor)
    {
        var (major, minor) = ParseBand(majorMinor);
        return installedRuntimes.Any(runtime =>
            runtime.Name == "Microsoft.NETCore.App" &&
            runtime.Version.Major == major &&
            runtime.Version.Minor == minor);
    }

    private static InstalledSdk[] ParseSdks(string listing) =>
        SplitLines(listing)
            .Select(line =>
            {
                var match = SdkLine().Match(line);
                if (!match.Success ||
                    !Version.TryParse(match.Groups["version"].Value, out var version) ||
                    version.Build < 0 ||
                    version.Revision >= 0)
                {
                    throw new FormatException($"Malformed .NET SDK listing line: {line}");
                }

                return new InstalledSdk(version, match.Groups["directory"].Value);
            })
            .ToArray();

    private static InstalledRuntime[] ParseRuntimes(string listing) =>
        SplitLines(listing)
            .Select(line =>
            {
                var match = RuntimeLine().Match(line);
                if (!match.Success ||
                    !Version.TryParse(match.Groups["version"].Value, out var version) ||
                    version.Build < 0 ||
                    version.Revision >= 0)
                {
                    throw new FormatException($"Malformed .NET runtime listing line: {line}");
                }

                return new InstalledRuntime(match.Groups["name"].Value, version, match.Groups["directory"].Value);
            })
            .ToArray();

    private static IEnumerable<string> SplitLines(string listing) =>
        listing.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line));

    private static (int Major, int Minor) ParseBand(string band)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(band);
        var parts = band.Split('.');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            major < 0 ||
            minor < 0)
        {
            throw new ArgumentException("SDK and runtime bands must use the major.minor format.", nameof(band));
        }

        return (major, minor);
    }

    private static void EnsureSucceeded(ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed with exit code {result.ExitCode}: {result.Executable} {string.Join(" ", result.Arguments)}{Environment.NewLine}{result.StandardError}");
        }
    }

    [GeneratedRegex("^(?<version>[^\\s]+)\\s+\\[(?<directory>.+)\\]$")]
    private static partial Regex SdkLine();

    [GeneratedRegex("^(?<name>[^\\s]+)\\s+(?<version>[^\\s]+)\\s+\\[(?<directory>.+)\\]$")]
    private static partial Regex RuntimeLine();
}

internal sealed record InstalledSdk(Version Version, string Directory);

internal sealed record InstalledRuntime(string Name, Version Version, string Directory);
