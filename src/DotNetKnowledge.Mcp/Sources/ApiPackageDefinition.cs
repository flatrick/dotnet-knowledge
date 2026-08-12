using System.Text.Json.Serialization;

namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// A pinned NuGet package that supplements an upstream source with API documentation assemblies.
/// </summary>
public sealed record ApiPackageDefinition(
    [property: JsonPropertyName("packageId")] string PackageId,
    [property: JsonPropertyName("assemblyName")] string AssemblyName,
    [property: JsonPropertyName("feed")] string Feed,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sha512")] string Sha512,
    [property: JsonPropertyName("defaultFramework")] string DefaultFramework);

internal static class NuGetPackageIdentity
{
    public static bool IsValidPackageId(string? packageId) =>
        !string.IsNullOrEmpty(packageId)
        && packageId.Length <= 100
        && packageId is not "." and not ".."
        && packageId.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    public static bool IsValidVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 256)
            return false;

        var metadataSeparator = version.IndexOf('+');
        if (metadataSeparator >= 0
            && (metadataSeparator != version.LastIndexOf('+')
                || !IsValidVersionLabelList(version.AsSpan(metadataSeparator + 1))))
        {
            return false;
        }

        var versionWithoutMetadata = metadataSeparator >= 0
            ? version.AsSpan(0, metadataSeparator)
            : version.AsSpan();
        var prereleaseSeparator = versionWithoutMetadata.IndexOf('-');
        if (prereleaseSeparator >= 0
            && !IsValidVersionLabelList(versionWithoutMetadata[(prereleaseSeparator + 1)..]))
        {
            return false;
        }

        var numericVersion = prereleaseSeparator >= 0
            ? versionWithoutMetadata[..prereleaseSeparator]
            : versionWithoutMetadata;
        var components = numericVersion.ToString().Split('.');
        return components.Length is >= 2 and <= 4
            && components.All(component =>
                component.Length > 0 && component.All(char.IsAsciiDigit));
    }

    private static bool IsValidVersionLabelList(ReadOnlySpan<char> labels)
    {
        if (labels.IsEmpty)
            return false;

        foreach (var label in labels.ToString().Split('.'))
        {
            if (label.Length == 0
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }
        }

        return true;
    }
}
