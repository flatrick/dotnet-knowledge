using System.Buffers.Binary;
using System.IO.Compression;

namespace DotNetKnowledge.Mcp.Sources;

public sealed record PackageFrameworkAsset(string Framework, string AssemblyEntry, string XmlEntry);

public static class PackageArchiveReader
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    private const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;
    private const int EndOfCentralDirectorySize = 22;
    private const int MaximumEndSearchBytes = EndOfCentralDirectorySize + ushort.MaxValue;

    private const long MaximumEntryBytes = 32L * 1024 * 1024;
    private const long MaximumTotalBytes = 128L * 1024 * 1024;

    // The pinned 5.3.0 package contains 95 entries. Ten times that count leaves ample package
    // evolution headroom while bounding central-directory-driven processing and path indexes.
    private const int MaximumArchiveEntries = 1024;

    public static IReadOnlyList<PackageFrameworkAsset> ReadAssets(
        string nupkgPath,
        ApiPackageDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        ArgumentNullException.ThrowIfNull(definition);

        using var packageStream = new FileStream(
            nupkgPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.RandomAccess);
        var declaredEntryCount = ReadDeclaredEntryCount(packageStream);
        if (declaredEntryCount > MaximumArchiveEntries)
            throw new InvalidDataException($"The package exceeds the {MaximumArchiveEntries}-entry limit.");

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        int actualEntryCount;
        try
        {
            actualEntryCount = archive.Entries.Count;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                "The package central directory does not match its declared entry count.",
                exception);
        }

        if ((ulong)actualEntryCount != declaredEntryCount)
            throw new InvalidDataException("The package central directory does not match its declared entry count.");
        if (actualEntryCount > MaximumArchiveEntries)
            throw new InvalidDataException($"The package exceeds the {MaximumArchiveEntries}-entry limit.");

        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new Dictionary<string, FrameworkEntries>(StringComparer.OrdinalIgnoreCase);
        var readBuffer = new byte[128 * 1024];
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var normalizedPath = NormalizeAndValidatePath(entry.FullName);
            if (!normalizedPaths.Add(normalizedPath))
                throw new InvalidDataException($"The package contains duplicate path '{normalizedPath}'.");

            ValidateEntrySize(entry, readBuffer, ref totalBytes);
            AddFrameworkEntry(normalizedPath, entry.FullName, definition.AssemblyName, frameworks);
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

    private static ulong ReadDeclaredEntryCount(FileStream stream)
    {
        if (stream.Length < EndOfCentralDirectorySize)
            throw new InvalidDataException("The package has no complete end-of-central-directory record.");

        var searchLength = (int)Math.Min(stream.Length, MaximumEndSearchBytes);
        var searchStart = stream.Length - searchLength;
        var endBuffer = new byte[searchLength];
        stream.Position = searchStart;
        stream.ReadExactly(endBuffer);

        var endOffsetInBuffer = FindEndOfCentralDirectory(endBuffer);
        if (endOffsetInBuffer < 0)
            throw new InvalidDataException("The package has no valid end-of-central-directory record.");

        var endRecord = endBuffer.AsSpan(endOffsetInBuffer, EndOfCentralDirectorySize);
        if (BinaryPrimitives.ReadUInt16LittleEndian(endRecord[4..]) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(endRecord[6..]) != 0)
        {
            throw new InvalidDataException("Multi-disk ZIP packages are not supported.");
        }

        var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[8..]);
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(endRecord[10..]);
        if (entriesOnDisk != ushort.MaxValue && totalEntries != ushort.MaxValue)
        {
            if (entriesOnDisk != totalEntries)
                throw new InvalidDataException("The package has inconsistent declared entry counts.");
            return totalEntries;
        }

        var absoluteEndOffset = searchStart + endOffsetInBuffer;
        return ReadZip64DeclaredEntryCount(stream, absoluteEndOffset);
    }

    private static int FindEndOfCentralDirectory(byte[] buffer)
    {
        for (var offset = buffer.Length - EndOfCentralDirectorySize; offset >= 0; offset--)
        {
            var record = buffer.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != EndOfCentralDirectorySignature)
                continue;

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
            if (offset + EndOfCentralDirectorySize + commentLength == buffer.Length)
                return offset;
        }

        return -1;
    }

    private static ulong ReadZip64DeclaredEntryCount(FileStream stream, long endOffset)
    {
        const int locatorSize = 20;
        const int minimumZip64EndSize = 56;
        var locatorOffset = endOffset - locatorSize;
        if (locatorOffset < 0)
            throw new InvalidDataException("The package ZIP64 locator is missing or truncated.");

        Span<byte> locator = stackalloc byte[locatorSize];
        stream.Position = locatorOffset;
        stream.ReadExactly(locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64EndOfCentralDirectoryLocatorSignature)
            throw new InvalidDataException("The package ZIP64 locator is missing or malformed.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1)
        {
            throw new InvalidDataException("Multi-disk ZIP64 packages are not supported.");
        }

        var zip64EndOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        if (zip64EndOffset > (ulong)locatorOffset
            || (ulong)locatorOffset - zip64EndOffset < minimumZip64EndSize)
        {
            throw new InvalidDataException("The package ZIP64 end-of-central-directory record is truncated.");
        }

        Span<byte> zip64End = stackalloc byte[minimumZip64EndSize];
        stream.Position = (long)zip64EndOffset;
        stream.ReadExactly(zip64End);
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip64End) != Zip64EndOfCentralDirectorySignature)
            throw new InvalidDataException("The package ZIP64 end-of-central-directory record is malformed.");

        var recordPayloadSize = BinaryPrimitives.ReadUInt64LittleEndian(zip64End[4..]);
        if (recordPayloadSize < 44
            || zip64EndOffset + 12 + recordPayloadSize != (ulong)locatorOffset)
        {
            throw new InvalidDataException("The package ZIP64 end-of-central-directory record is truncated.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(zip64End[16..]) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(zip64End[20..]) != 0)
        {
            throw new InvalidDataException("Multi-disk ZIP64 packages are not supported.");
        }

        var entriesOnDisk = BinaryPrimitives.ReadUInt64LittleEndian(zip64End[24..]);
        var totalEntries = BinaryPrimitives.ReadUInt64LittleEndian(zip64End[32..]);
        if (entriesOnDisk != totalEntries)
            throw new InvalidDataException("The package ZIP64 record has inconsistent declared entry counts.");

        return totalEntries;
    }

    private static string NormalizeAndValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\0'))
            throw new InvalidDataException("The package contains an empty or invalid entry path.");
        if (path.EndsWith('/') || path.EndsWith('\\'))
            throw new InvalidDataException($"The package contains non-file entry path '{path}'.");

        var normalizedSeparators = path.Replace('\\', '/');
        if (normalizedSeparators.StartsWith('/')
            || HasDrivePrefix(normalizedSeparators))
        {
            throw new InvalidDataException($"The package contains rooted path '{path}'.");
        }

        var segments = normalizedSeparators.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"The package contains aliased or traversing path '{path}'.");

        return normalizedSeparators;
    }

    private static bool HasDrivePrefix(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static void ValidateEntrySize(ZipArchiveEntry entry, byte[] buffer, ref long totalBytes)
    {
        using var stream = entry.Open();
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
        string rawPath,
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
            entries.AssemblyEntry = rawPath;
        }
        else
        {
            if (entries.XmlEntry is not null)
                throw new InvalidDataException($"Framework '{entries.Framework}' contains duplicate XML documents.");
            entries.XmlEntry = rawPath;
        }
    }

    private sealed class FrameworkEntries(string framework)
    {
        public string Framework { get; } = framework;

        public string? AssemblyEntry { get; set; }

        public string? XmlEntry { get; set; }
    }
}
