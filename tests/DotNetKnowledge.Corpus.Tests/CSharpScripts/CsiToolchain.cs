using System.Diagnostics;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

internal sealed class CsiToolchain
{
    private const string RequiredVersion = "5.6.0";

    private readonly Func<bool> isWindows;
    private readonly Func<string, string?> getProductVersion;

    public CsiToolchain()
        : this(
            OperatingSystem.IsWindows,
            path => FileVersionInfo.GetVersionInfo(path).ProductVersion)
    {
    }

    internal CsiToolchain(Func<bool> isWindows, Func<string, string?> getProductVersion)
    {
        this.isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
        this.getProductVersion = getProductVersion ?? throw new ArgumentNullException(nameof(getProductVersion));
    }

    public bool TryResolve(string baseDirectory, out string path, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        path = string.Empty;
        if (!isWindows())
        {
            reason =
                "Pinned csi 5.6.0 is unavailable: the Microsoft.Net.Compilers.Toolset net472 host requires Windows.";
            return false;
        }

        var candidate = Path.Combine(baseDirectory, "Csi", "csi.exe");
        if (!File.Exists(candidate))
        {
            reason =
                $"Pinned csi 5.6.0 executable was not found at '{candidate}'. " +
                "Restore the test project to copy the net472 toolset assets.";
            return false;
        }

        string? productVersion;
        try
        {
            productVersion = getProductVersion(candidate);
        }
        catch (Exception exception)
        {
            reason =
                $"Pinned csi executable '{candidate}' could not be inspected for product version 5.6.0: " +
                $"{exception.GetType().FullName}: {exception.Message}";
            return false;
        }

        if (!HasRequiredVersion(productVersion))
        {
            reason =
                $"Pinned csi executable '{candidate}' must report product version 5.6.0, " +
                $"but reported '{productVersion ?? "(missing)"}'.";
            return false;
        }

        path = candidate;
        reason = string.Empty;
        return true;
    }

    private static bool HasRequiredVersion(string? productVersion)
    {
        return productVersion is not null &&
               productVersion.StartsWith(RequiredVersion, StringComparison.Ordinal) &&
               (productVersion.Length == RequiredVersion.Length ||
                !char.IsDigit(productVersion[RequiredVersion.Length]));
    }
}
