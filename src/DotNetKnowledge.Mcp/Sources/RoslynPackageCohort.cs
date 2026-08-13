using System.Text.Json;

namespace DotNetKnowledge.Mcp.Sources;

public static class RoslynPackageCohort
{
    private const string ManifestPrefix = "roslyn-dotnet-";

    public static string Read(
        string repositoryDirectory,
        IReadOnlyList<ApiPackageDefinition> packages,
        bool pinned)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryDirectory);
        ArgumentNullException.ThrowIfNull(packages);

        var manifestsDirectory = Path.Combine(
            Path.GetFullPath(repositoryDirectory),
            "dotnet",
            "xml",
            "PackageInformation");
        if (!Directory.Exists(manifestsDirectory))
            throw new InvalidDataException("The synchronized Roslyn source has no package-information directory.");

        var candidates = Directory.EnumerateFiles(manifestsDirectory, $"{ManifestPrefix}*.json")
            .Select(TryCreateCandidate)
            .OfType<ManifestCandidate>()
            .OrderByDescending(candidate => candidate.NumericVersion)
            .ThenByDescending(candidate => candidate.Version, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException("The synchronized Roslyn source has no valid package-cohort manifest.");
        if (candidates.Length > 1 && candidates[0].NumericVersion == candidates[1].NumericVersion)
            throw new InvalidDataException("The synchronized Roslyn source has ambiguous package-cohort manifests.");

        var selected = candidates[0];
        var packageVersion = ValidateManifest(selected);
        if (pinned && packages.Any(package =>
            !string.Equals(package.Version, packageVersion, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"The Roslyn package cohort '{packageVersion}' does not match the pinned package catalog.");
        }

        return packageVersion;
    }

    private static ManifestCandidate? TryCreateCandidate(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.StartsWith(ManifestPrefix, StringComparison.Ordinal))
            return null;

        var version = fileName[ManifestPrefix.Length..];
        return TryParseNumericVersion(version, out var numericVersion)
            ? new ManifestCandidate(path, version, numericVersion)
            : null;
    }

    private static string ValidateManifest(ManifestCandidate candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(candidate.Path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetPropertyCount() != 1)
                throw InvalidManifest(candidate.Path);

            var cohort = root.EnumerateObject().Single();
            if (!string.Equals(cohort.Name, ManifestPrefix + candidate.Version, StringComparison.Ordinal)
                || cohort.Value.ValueKind != JsonValueKind.Object
                || cohort.Value.GetPropertyCount() == 0)
            {
                throw InvalidManifest(candidate.Path);
            }

            string? agreedVersion = null;
            foreach (var package in cohort.Value.EnumerateObject())
            {
                if (package.Value.ValueKind != JsonValueKind.Object
                    || !package.Value.TryGetProperty("Version", out var version)
                    || version.ValueKind != JsonValueKind.String
                    || version.GetString() is not { } packageVersion
                    || !TryParseNumericVersion(packageVersion, out _)
                    || agreedVersion is not null
                        && !string.Equals(packageVersion, agreedVersion, StringComparison.Ordinal))
                {
                    throw InvalidManifest(candidate.Path);
                }

                agreedVersion ??= packageVersion;
            }

            return agreedVersion ?? throw InvalidManifest(candidate.Path);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Roslyn package-cohort manifest '{candidate.Path}' is malformed.", exception);
        }
    }

    private static InvalidDataException InvalidManifest(string path) =>
        new($"Roslyn package-cohort manifest '{path}' is internally inconsistent.");

    private static bool TryParseNumericVersion(string version, out Version numericVersion)
    {
        numericVersion = null!;
        if (version.Length > 0
            && version.All(character => char.IsAsciiDigit(character) || character == '.')
            && Version.TryParse(version, out var parsed))
        {
            numericVersion = parsed;
            return true;
        }

        return false;
    }

    private sealed record ManifestCandidate(string Path, string Version, Version NumericVersion);
}
