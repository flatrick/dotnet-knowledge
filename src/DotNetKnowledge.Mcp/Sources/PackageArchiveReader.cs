using System.IO.Compression;

namespace DotNetKnowledge.Mcp.Sources;

public sealed record PackageFrameworkAsset(string Framework, string AssemblyEntry, string XmlEntry);

public static class PackageArchiveReader
{
    private const long MaximumEntryBytes = 32L * 1024 * 1024;
    private const long MaximumTotalBytes = 128L * 1024 * 1024;

    public static IReadOnlyList<PackageFrameworkAsset> ReadAssets(
        string nupkgPath,
        ApiPackageDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        ArgumentNullException.ThrowIfNull(definition);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new Dictionary<string, FrameworkEntries>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var normalizedPath = NormalizeAndValidatePath(entry.FullName);
            if (!normalizedPaths.Add(normalizedPath))
                throw new InvalidDataException($"The package contains duplicate path '{normalizedPath}'.");

            ValidateEntrySize(entry, ref totalBytes);
            AddFrameworkEntry(normalizedPath, definition.AssemblyName, frameworks);
        }

        if (frameworks.Count == 0)
        {
            throw new InvalidDataException(
                $"The package has no lib framework assets named '{definition.AssemblyName}'.");
        }

        var assets = new List<PackageFrameworkAsset>(frameworks.Count);
        foreach (var entries in frameworks.Values)
        {
            if (entries.AssemblyEntry is null || entries.XmlEntry is null)
            {
                throw new InvalidDataException(
                    $"Framework '{entries.Framework}' does not contain a paired assembly and XML document.");
            }

            assets.Add(new PackageFrameworkAsset(
                entries.Framework,
                entries.AssemblyEntry,
                entries.XmlEntry));
        }

        assets.Sort(static (left, right) => string.CompareOrdinal(left.Framework, right.Framework));
        return assets;
    }

    private static string NormalizeAndValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\0'))
            throw new InvalidDataException("The package contains an empty or invalid entry path.");

        var normalizedSeparators = path.Replace('\\', '/');
        if (normalizedSeparators.StartsWith('/')
            || HasDrivePrefix(normalizedSeparators))
        {
            throw new InvalidDataException($"The package contains rooted path '{path}'.");
        }

        var segments = normalizedSeparators.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            throw new InvalidDataException($"The package contains parent traversal path '{path}'.");

        var normalizedSegments = segments.Where(segment => segment != ".").ToArray();
        if (normalizedSegments.Length == 0)
            throw new InvalidDataException($"The package entry path '{path}' has no filename.");

        return string.Join('/', normalizedSegments);
    }

    private static bool HasDrivePrefix(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static void ValidateEntrySize(ZipArchiveEntry entry, ref long totalBytes)
    {
        using var stream = entry.Open();
        var buffer = new byte[128 * 1024];
        long entryBytes = 0;
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            entryBytes += count;
            totalBytes += count;
            if (entryBytes > MaximumEntryBytes)
            {
                throw new InvalidDataException(
                    $"Package entry '{entry.FullName}' exceeds the 32 MiB uncompressed limit.");
            }

            if (totalBytes > MaximumTotalBytes)
                throw new InvalidDataException("The package exceeds the 128 MiB total uncompressed limit.");
        }
    }

    private static void AddFrameworkEntry(
        string normalizedPath,
        string assemblyName,
        Dictionary<string, FrameworkEntries> frameworks)
    {
        var segments = normalizedPath.Split('/');
        if (segments.Length != 3 || !segments[0].Equals("lib", StringComparison.OrdinalIgnoreCase))
            return;

        var assemblyFileName = $"{assemblyName}.dll";
        var xmlFileName = $"{assemblyName}.xml";
        var isAssembly = segments[2].Equals(assemblyFileName, StringComparison.OrdinalIgnoreCase);
        var isXml = segments[2].Equals(xmlFileName, StringComparison.OrdinalIgnoreCase);
        if (!isAssembly && !isXml)
            return;

        if (!frameworks.TryGetValue(segments[1], out var entries))
        {
            entries = new FrameworkEntries(segments[1]);
            frameworks.Add(entries.Framework, entries);
        }

        if (isAssembly)
        {
            if (entries.AssemblyEntry is not null)
                throw new InvalidDataException($"Framework '{entries.Framework}' contains duplicate assemblies.");
            entries.AssemblyEntry = normalizedPath;
        }
        else
        {
            if (entries.XmlEntry is not null)
                throw new InvalidDataException($"Framework '{entries.Framework}' contains duplicate XML documents.");
            entries.XmlEntry = normalizedPath;
        }
    }

    private sealed class FrameworkEntries(string framework)
    {
        public string Framework { get; } = framework;

        public string? AssemblyEntry { get; set; }

        public string? XmlEntry { get; set; }
    }
}
