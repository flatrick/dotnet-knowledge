using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class PackageArchiveReaderTests
{
    private const int MiB = 1024 * 1024;
    private const int ExpectedMaximumArchiveEntries = 1024;

    [TestMethod]
    public void ReadAssetsReturnsPairedFrameworksInOrdinalOrder()
    {
        var path = CreateArchive(
            ("lib/net8.0/Test.Assembly.dll", Encoding.UTF8.GetBytes("repository-authored net8 dll")),
            ("lib/net8.0/Test.Assembly.xml", Encoding.UTF8.GetBytes("<doc>repository-authored net8 xml</doc>")),
            ("lib/net10.0/Test.Assembly.dll", Encoding.UTF8.GetBytes("repository-authored net10 dll")),
            ("lib/net10.0/Test.Assembly.xml", Encoding.UTF8.GetBytes("<doc>repository-authored net10 xml</doc>")),
            ("tools/ignored.exe", Encoding.UTF8.GetBytes("ignored")));
        try
        {
            var assets = PackageArchiveReader.ReadAssets(path, Package());

            var expectedFrameworks = new List<string> { "net10.0", "net8.0" };
            CollectionAssert.AreEqual(expectedFrameworks, assets.Select(asset => asset.Framework).ToList());
            Assert.AreEqual("lib/net8.0/Test.Assembly.dll", assets[1].AssemblyEntry);
            Assert.AreEqual("lib/net8.0/Test.Assembly.xml", assets[1].XmlEntry);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("/lib/net8.0/Test.Assembly.dll")]
    [DataRow("\\lib\\net8.0\\Test.Assembly.dll")]
    [DataRow("C:\\lib\\net8.0\\Test.Assembly.dll")]
    [DataRow("../outside.bin")]
    [DataRow("lib/../outside.bin")]
    [DataRow("..\\outside.bin")]
    [DataRow("lib\\..\\outside.bin")]
    public void ReadAssetsRejectsUnsafeEntryPaths(string unsafePath)
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", [1]),
            ("lib/net10.0/Test.Assembly.xml", [2]),
            (unsafePath, [3]));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsCaseInsensitiveDuplicateNormalizedPaths()
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", [1]),
            ("lib/net10.0/Test.Assembly.xml", [2]),
            ("LIB\\.\\NET10.0\\test.assembly.DLL", [3]));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsAnEntryOverThirtyTwoMiB()
    {
        var path = CreateSizedArchive(("oversized.bin", 32L * MiB + 1));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsTotalContentOverOneHundredTwentyEightMiB()
    {
        var path = CreateSizedArchive(
            ("part-1.bin", 26L * MiB),
            ("part-2.bin", 26L * MiB),
            ("part-3.bin", 26L * MiB),
            ("part-4.bin", 26L * MiB),
            ("part-5.bin", 26L * MiB));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("lib/net10.0/Test.Assembly.dll")]
    [DataRow("lib/net10.0/Test.Assembly.xml")]
    public void ReadAssetsRejectsAMissingAssemblyOrXmlPair(string onlyEntry)
    {
        var path = CreateArchive((onlyEntry, new byte[] { 1 }));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsDuplicateFrameworkPairs()
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", [1]),
            ("lib/net10.0/Test.Assembly.xml", [2]),
            ("lib/NET10.0/Test.Assembly.dll", [3]),
            ("lib/NET10.0/Test.Assembly.xml", [4]));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsAnArchiveWithOnlyTheWrongBasename()
    {
        var path = CreateArchive(
            ("lib/net10.0/Wrong.Assembly.dll", [1]),
            ("lib/net10.0/Wrong.Assembly.xml", [2]));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsPreservesRawMixedSeparatorNamesThatReopenExactEntries()
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", Encoding.UTF8.GetBytes("raw dll")),
            ("lib\\net10.0\\Test.Assembly.xml", Encoding.UTF8.GetBytes("raw xml")));
        try
        {
            var asset = PackageArchiveReader.ReadAssets(path, Package()).Single();

            Assert.AreEqual("lib/net10.0/Test.Assembly.dll", asset.AssemblyEntry);
            Assert.AreEqual("lib\\net10.0\\Test.Assembly.xml", asset.XmlEntry);
            using var archive = ZipFile.OpenRead(path);
            Assert.IsNotNull(archive.GetEntry(asset.AssemblyEntry));
            Assert.IsNotNull(archive.GetEntry(asset.XmlEntry));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("lib/net10.0/")]
    [DataRow("lib\\net10.0\\")]
    public void ReadAssetsRejectsDirectoryEntriesAndTrailingSeparators(string directoryPath)
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", [1]),
            ("lib/net10.0/Test.Assembly.xml", [2]),
            (directoryPath, []));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("lib/./net10.0/ignored.bin")]
    [DataRow("lib//net10.0/ignored.bin")]
    [DataRow("lib\\.\\net10.0\\ignored.bin")]
    [DataRow("lib\\\\net10.0\\ignored.bin")]
    public void ReadAssetsRejectsDotAndEmptySegmentAliases(string aliasPath)
    {
        var path = CreateArchive(
            ("lib/net10.0/Test.Assembly.dll", [1]),
            ("lib/net10.0/Test.Assembly.xml", [2]),
            (aliasPath, [3]));
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsAcceptsTheMaximumArchiveEntryCount()
    {
        var path = CreateEntryCountArchive(ExpectedMaximumArchiveEntries);
        try
        {
            Assert.HasCount(1, PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsMoreThanTheMaximumArchiveEntryCount()
    {
        var path = CreateEntryCountArchive(ExpectedMaximumArchiveEntries + 1);
        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() => PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsDeclaredStandardEntryExcessBeforeParsingTheCentralDirectory()
    {
        var path = CreateDeclaredOnlyStandardArchive(ExpectedMaximumArchiveEntries + 1);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "1024-entry limit");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsAcceptsAValidZip64ArchiveAtTheEntryBoundary()
    {
        var path = CreateEntryCountArchive(ExpectedMaximumArchiveEntries);
        ConvertToZip64(path, ExpectedMaximumArchiveEntries);
        try
        {
            Assert.HasCount(1, PackageArchiveReader.ReadAssets(path, Package()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsDeclaredZip64EntryExcessBeforeParsingTheCentralDirectory()
    {
        var path = CreateEntryCountArchive(2);
        ConvertToZip64(path, ExpectedMaximumArchiveEntries + 1UL);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "1024-entry limit");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsZip64SentinelsWithoutALocator()
    {
        var path = CreateEntryCountArchive(2);
        RewriteStandardEntryCount(path, ushort.MaxValue);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "ZIP64 locator");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsATruncatedZip64CountRecord()
    {
        var path = CreateEntryCountArchive(2);
        ConvertToZip64(path, 2);
        TruncateZip64Record(path);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "ZIP64 end-of-central-directory record");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsADeclaredActualEntryCountMismatch()
    {
        var path = CreateEntryCountArchive(2);
        RewriteStandardEntryCount(path, 1);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "declared entry count");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadAssetsRejectsAFalseLowZip64CountThatWouldBypassTheCap()
    {
        var path = CreateEntryCountArchive(ExpectedMaximumArchiveEntries + 1);
        ConvertToZip64(path, 1);
        try
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageArchiveReader.ReadAssets(path, Package()));

            StringAssert.Contains(exception.Message, "declared entry count");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ApiPackageDefinition Package() => new(
        "Test.Package",
        "Test.Assembly",
        "https://feed.test/v3/index.json",
        "5.3.0",
        Convert.ToBase64String(new byte[64]),
        "net10.0");

    private static string CreateArchive(params (string Path, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}.nupkg");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (entryPath, bytes) in entries)
        {
            using var stream = archive.CreateEntry(entryPath, CompressionLevel.SmallestSize).Open();
            stream.Write(bytes);
        }

        return path;
    }

    private static string CreateSizedArchive(params (string Path, long Length)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}.nupkg");
        var buffer = new byte[128 * 1024];
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (entryPath, length) in entries)
        {
            using var stream = archive.CreateEntry(entryPath, CompressionLevel.SmallestSize).Open();
            for (long remaining = length; remaining > 0;)
            {
                var count = (int)Math.Min(buffer.Length, remaining);
                stream.Write(buffer, 0, count);
                remaining -= count;
            }
        }

        return path;
    }

    private static string CreateEntryCountArchive(int entryCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}.nupkg");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        archive.CreateEntry("lib/net10.0/Test.Assembly.dll");
        archive.CreateEntry("lib/net10.0/Test.Assembly.xml");
        for (var index = 2; index < entryCount; index++)
            archive.CreateEntry($"content/{index:D4}.bin");

        return path;
    }

    private static string CreateDeclaredOnlyStandardArchive(int declaredEntryCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}.nupkg");
        var endRecord = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(endRecord, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(8), (ushort)declaredEntryCount);
        BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(10), (ushort)declaredEntryCount);
        File.WriteAllBytes(path, endRecord);
        return path;
    }

    private static void RewriteStandardEntryCount(string path, ushort declaredEntryCount)
    {
        var bytes = File.ReadAllBytes(path);
        var endOffset = FindSignature(bytes, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(endOffset + 8), declaredEntryCount);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(endOffset + 10), declaredEntryCount);
        File.WriteAllBytes(path, bytes);
    }

    private static void ConvertToZip64(string path, ulong declaredEntryCount)
    {
        var bytes = File.ReadAllBytes(path);
        var endOffset = FindSignature(bytes, 0x06054b50);
        var standardEnd = bytes.AsSpan(endOffset, 22).ToArray();
        var centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(standardEnd.AsSpan(12));
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(standardEnd.AsSpan(16));

        var zip64End = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(zip64End, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64End.AsSpan(4), 44);
        BinaryPrimitives.WriteUInt16LittleEndian(zip64End.AsSpan(12), 45);
        BinaryPrimitives.WriteUInt16LittleEndian(zip64End.AsSpan(14), 45);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64End.AsSpan(24), declaredEntryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64End.AsSpan(32), declaredEntryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64End.AsSpan(40), centralDirectorySize);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64End.AsSpan(48), centralDirectoryOffset);

        var locator = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)endOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1);

        BinaryPrimitives.WriteUInt16LittleEndian(standardEnd.AsSpan(8), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(standardEnd.AsSpan(10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(standardEnd.AsSpan(12), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(standardEnd.AsSpan(16), uint.MaxValue);

        using var output = new MemoryStream(bytes.Length + zip64End.Length + locator.Length);
        output.Write(bytes, 0, endOffset);
        output.Write(zip64End);
        output.Write(locator);
        output.Write(standardEnd);
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void TruncateZip64Record(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var locatorOffset = FindSignature(bytes, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(locatorOffset + 8), (ulong)(locatorOffset - 8));
        File.WriteAllBytes(path, bytes);
    }

    private static int FindSignature(byte[] bytes, uint signature)
    {
        for (var index = bytes.Length - 4; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) == signature)
                return index;
        }

        Assert.Fail($"ZIP signature 0x{signature:X8} was not found.");
        return -1;
    }
}
