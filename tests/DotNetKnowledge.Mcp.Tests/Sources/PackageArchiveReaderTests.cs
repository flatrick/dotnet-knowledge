using System.IO.Compression;
using System.Text;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class PackageArchiveReaderTests
{
    private const int MiB = 1024 * 1024;

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
}
