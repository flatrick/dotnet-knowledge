using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class PackageApiCorpusBuilderTests
{
    private static readonly string FixtureAssemblyPath = GetFixturePath("ApiFixtureAssemblyPath");
    private static readonly string[] ExpectedFrameworks = ["net10.0", "net8.0"];
    private static readonly string[] ExpectedArrayShapeProbeParameterNames = ["values", "marker"];

    [TestMethod]
    public async Task BuildAsyncJoinsOnlyVisibleMetadataAndWritesDeterministicFrameworkFiles()
    {
        var packagePath = CreatePackage();
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        try
        {
            var builder = new PackageApiCorpusBuilder();
            var definition = Package();
            var first = await builder.BuildAsync(packagePath, definition, output, CancellationToken.None);
            var firstHash = Hash(Path.Combine(output, "net10.0.json"));

            var second = await builder.BuildAsync(packagePath, definition, output, CancellationToken.None);

            CollectionAssert.AreEqual(ExpectedFrameworks, first.AvailableFrameworks.ToArray());
            Assert.IsTrue(File.Exists(Path.Combine(output, "net10.0.json")));
            Assert.AreEqual(firstHash, Hash(Path.Combine(output, "net10.0.json")));
            Assert.AreEqual(first.CorpusFiles["net10.0"], second.CorpusFiles["net10.0"]);
            var corpus = PackageApiCorpusStore.Read(first.CorpusFiles["net10.0"], definition, "net10.0");
            var type = corpus.Types.Single(item => item.FullName == "Fixtures.SignatureGallery<T>");
            Assert.IsTrue(type.Members.Any(member => member.Name == "PublicOnly" && member.Documentation.Summary is null));
            Assert.IsFalse(type.Members.Any(member => member.Name == "InternalOnly"));
            Assert.AreEqual("Creates a gallery from XML.", type.Documentation.Summary);
            AssertCorpusIsOrdinallyOrdered(corpus);
            var arrayShapeProbe = type.Members.Single(member => member.Name == "ArrayShapeProbe");
            CollectionAssert.AreEqual(
                ExpectedArrayShapeProbeParameterNames, arrayShapeProbe.Parameters.Select(parameter => parameter.Name).ToArray());
            var json = File.ReadAllText(first.CorpusFiles["net10.0"]);
            StringAssert.Contains(json, "\"SchemaVersion\":1");
            Assert.IsFalse(json.Contains(Environment.NewLine, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsyncRejectsAFrameworkAssemblyWithTheWrongIdentity()
    {
        var packagePath = CreatePackage(wrongAssemblyIdentity: true);
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new PackageApiCorpusBuilder().BuildAsync(packagePath, Package(), output, CancellationToken.None));
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsyncLeavesAnExistingCorpusUntouchedWhenALaterFrameworkFails()
    {
        var packagePath = CreatePackage(wrongAssemblyFramework: "net8.0");
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(output);
            var oldNet10 = Path.Combine(output, "net10.0.json");
            var oldNet8 = Path.Combine(output, "net8.0.json");
            File.WriteAllText(oldNet10, "old-net10");
            File.WriteAllText(oldNet8, "old-net8");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new PackageApiCorpusBuilder().BuildAsync(packagePath, Package(), output, CancellationToken.None));

            Assert.AreEqual("old-net10", File.ReadAllText(oldNet10));
            Assert.AreEqual("old-net8", File.ReadAllText(oldNet8));
            Assert.AreEqual(
                0,
                Directory.GetDirectories(Path.GetDirectoryName(output)!, "." + Path.GetFileName(output) + ".*").Length);
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreRejectsMismatchedIdentityAndSchemaAndIsolatesCachedHashes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var first = Package("first");
            var second = first with { Sha512 = Convert.ToBase64String(Enumerable.Repeat((byte)2, 64).ToArray()) };
            var firstPath = Path.Combine(directory, "first.json");
            var secondPath = Path.Combine(directory, "second.json");
            await PackageApiCorpusStore.WriteAsync(firstPath, first, "net10.0", Corpus("First"), CancellationToken.None);
            await PackageApiCorpusStore.WriteAsync(secondPath, second, "net10.0", Corpus("Second"), CancellationToken.None);

            Assert.AreEqual("First", PackageApiCorpusStore.Read(firstPath, first, "net10.0").Types.Single().Name);
            Assert.AreEqual("Second", PackageApiCorpusStore.Read(secondPath, second, "net10.0").Types.Single().Name);
            Assert.ThrowsExactly<InvalidDataException>(() => PackageApiCorpusStore.Read(firstPath, first with { PackageId = "Other.Package" }, "net10.0"));
            Assert.ThrowsExactly<InvalidDataException>(() => PackageApiCorpusStore.Read(firstPath, first with { Version = "2.0.0" }, "net10.0"));
            Assert.ThrowsExactly<InvalidDataException>(() => PackageApiCorpusStore.Read(firstPath, first with { Sha512 = second.Sha512 }, "net10.0"));
            Assert.ThrowsExactly<InvalidDataException>(() => PackageApiCorpusStore.Read(firstPath, first, "net8.0"));

            var invalidSchema = Path.Combine(directory, "invalid-schema.json");
            File.WriteAllText(invalidSchema, JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, first.PackageId, first.Version, first.Sha512, Framework = "net10.0", Corpus = Corpus("Invalid"),
            }));
            Assert.ThrowsExactly<InvalidDataException>(() => PackageApiCorpusStore.Read(invalidSchema, first, "net10.0"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreCleansUpTemporaryOutputWhenCanceled()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "net10.0.json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            try
            {
                await PackageApiCorpusStore.WriteAsync(
                    path, Package("cancel"), "net10.0", Corpus("Canceled"), cancellation.Token);
                Assert.Fail("Writing with a canceled token did not fail.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsyncCancellationAfterFrameworkStagingRetainsTheExistingCorpus()
    {
        var packagePath = CreatePackage();
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(output);
            var oldNet10 = Path.Combine(output, "net10.0.json");
            var oldNet8 = Path.Combine(output, "net8.0.json");
            var expectedNet10 = new byte[] { 0, 1, 255, 2 };
            var expectedNet8 = new byte[] { 255, 3, 0, 4 };
            File.WriteAllBytes(oldNet10, expectedNet10);
            File.WriteAllBytes(oldNet8, expectedNet8);

            var builder = new PackageApiCorpusBuilder(framework =>
            {
                Assert.AreEqual("net10.0", framework);
                cancellation.Cancel();
            });
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                builder.BuildAsync(packagePath, Package(), output, cancellation.Token));

            CollectionAssert.AreEqual(expectedNet10, File.ReadAllBytes(oldNet10));
            CollectionAssert.AreEqual(expectedNet8, File.ReadAllBytes(oldNet8));
            Assert.AreEqual(0, TemporarySiblingDirectories(output).Length);
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsyncAcceptsAnOutputDirectoryWithATrailingSeparator()
    {
        var packagePath = CreatePackage();
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        try
        {
            var result = await new PackageApiCorpusBuilder().BuildAsync(
                packagePath, Package(), output + Path.DirectorySeparatorChar, CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(output, "net10.0.json")));
            Assert.AreEqual(Path.Combine(output, "net10.0.json"), result.CorpusFiles["net10.0"]);
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreReturnsTheCurrentCorpusWhenAnIdentityMatchedFileIsRewritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "net10.0.json");
        var definition = Package("rewrite");
        try
        {
            await PackageApiCorpusStore.WriteAsync(path, definition, "net10.0", Corpus("First"), CancellationToken.None);
            Assert.AreEqual("First", PackageApiCorpusStore.Read(path, definition, "net10.0").Types.Single().Name);

            await PackageApiCorpusStore.WriteAsync(path, definition, "net10.0", Corpus("Second"), CancellationToken.None);

            Assert.AreEqual("Second", PackageApiCorpusStore.Read(path, definition, "net10.0").Types.Single().Name);
            Assert.AreEqual(1, PackageApiCorpusStore.GetCachedVariantCount(definition, "net10.0"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreDoesNotConflateDifferentFilesWithTheSameIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var definition = Package("same-key");
        try
        {
            var first = Path.Combine(directory, "first.json");
            var second = Path.Combine(directory, "second.json");
            await PackageApiCorpusStore.WriteAsync(first, definition, "net10.0", Corpus("First"), CancellationToken.None);
            await PackageApiCorpusStore.WriteAsync(second, definition, "net10.0", Corpus("Second"), CancellationToken.None);

            Assert.AreEqual("First", PackageApiCorpusStore.Read(first, definition, "net10.0").Types.Single().Name);
            Assert.AreEqual("Second", PackageApiCorpusStore.Read(second, definition, "net10.0").Types.Single().Name);
            Assert.AreEqual(1, PackageApiCorpusStore.GetCachedVariantCount(definition, "net10.0"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsyncRestoresThePriorCorpusAfterARealFileCollisionBeforeCommit()
    {
        var packagePath = CreatePackage();
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        var oldNet10 = Path.Combine(output, "net10.0.json");
        var oldNet8 = Path.Combine(output, "net8.0.json");
        var expectedNet10 = new byte[] { 0, 255, 1 };
        var expectedNet8 = new byte[] { 1, 0, 255 };
        File.WriteAllBytes(oldNet10, expectedNet10);
        File.WriteAllBytes(oldNet8, expectedNet8);
        try
        {
            var builder = new PackageApiCorpusBuilder(afterOldOutputMoved: () =>
                File.WriteAllBytes(output, new byte[] { 9, 8, 7 }));
            await Assert.ThrowsExactlyAsync<IOException>(() =>
                builder.BuildAsync(packagePath, Package(), output, CancellationToken.None));

            CollectionAssert.AreEqual(expectedNet10, File.ReadAllBytes(oldNet10));
            CollectionAssert.AreEqual(expectedNet8, File.ReadAllBytes(oldNet8));
            var conflict = Directory.GetFiles(
                Path.GetDirectoryName(output)!, "." + Path.GetFileName(output) + ".*.conflict").Single();
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, File.ReadAllBytes(conflict));
            Assert.AreEqual(0, TemporarySiblingDirectories(output).Length);
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            foreach (var conflict in Directory.GetFiles(
                Path.GetDirectoryName(output)!, "." + Path.GetFileName(output) + ".*.conflict"))
            {
                File.Delete(conflict);
            }
        }
    }

    [TestMethod]
    public async Task BuildAsyncPrunesRetiredBackupsAfterSuccessfulPublication()
    {
        var packagePath = CreatePackage();
        var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-corpus-{Guid.NewGuid():N}");
        try
        {
            var builder = new PackageApiCorpusBuilder();
            await builder.BuildAsync(packagePath, Package(), output, CancellationToken.None);
            await builder.BuildAsync(packagePath, Package(), output, CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(output, "net10.0.json")));
            Assert.AreEqual(
                0,
                Directory.GetDirectories(
                    Path.GetDirectoryName(output)!, "." + Path.GetFileName(output) + ".*.backup").Length);
        }
        finally
        {
            File.Delete(packagePath);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static ApiPackageDefinition Package(string suffix = "fixture") => new(
        $"Fixture.Package.{suffix}", "DotNetKnowledge.Mcp.Tests.ApiFixture", "https://feed.test/v3/index.json", "1.0.0",
        Convert.ToBase64String(new byte[64]), "net10.0");

    private static string CreatePackage(bool wrongAssemblyIdentity = false, string? wrongAssemblyFramework = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-package-{Guid.NewGuid():N}.nupkg");
        const string docs = """
            <doc><members>
            <member name="T:Fixtures.SignatureGallery`1"><summary>Creates a gallery from XML.</summary></member>
            <member name="M:Fixtures.SignatureGallery`1.ArrayShapeProbe(System.Int32[],System.String)">
            <summary>Checks named documentation ordering.</summary><param name="values">Values.</param>
            <param name="marker">Marker.</param><exception cref="T:System.ZException">Zed.</exception>
            <exception cref="T:System.AException">Aye.</exception></member>
            <member name="M:Fixtures.SignatureGallery`1.InternalOnly"><summary>Must not create an API.</summary></member>
            </members></doc>
            """;
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var framework in new[] { "net8.0", "net10.0" })
        {
            using (var assembly = archive.CreateEntry(
                $"lib/{framework}/DotNetKnowledge.Mcp.Tests.ApiFixture.dll").Open())
            {
                var source = wrongAssemblyIdentity || framework == wrongAssemblyFramework
                    ? typeof(PackageApiCorpusBuilderTests).Assembly.Location
                    : FixtureAssemblyPath;
                assembly.Write(File.ReadAllBytes(source));
            }
            using var xml = new StreamWriter(archive.CreateEntry($"lib/{framework}/DotNetKnowledge.Mcp.Tests.ApiFixture.xml").Open(), Encoding.UTF8, leaveOpen: false);
            xml.Write(docs);
        }
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string[] TemporarySiblingDirectories(string output) => Directory.GetDirectories(
        Path.GetDirectoryName(output)!, "." + Path.GetFileName(output) + ".*.tmp");

    private static ApiCorpus Corpus(string name) => new(1,
        new[] { new ApiCorpusType("T:Fixture.Type", name, $"Fixture.{name}", null, [], [], [], EmptyDocumentation(), []) });

    private static ApiDocumentation EmptyDocumentation() => new(null, [], [], null, null, null, []);

    private static void AssertCorpusIsOrdinallyOrdered(ApiCorpus corpus)
    {
        CollectionAssert.AreEqual(
            corpus.Types.OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type => type.FullName).ToArray(),
            corpus.Types.Select(type => type.FullName).ToArray());
        foreach (var type in corpus.Types)
        {
            CollectionAssert.AreEqual(
                type.Members.OrderBy(member => member.EcmaId, StringComparer.Ordinal).Select(member => member.EcmaId).ToArray(),
                type.Members.Select(member => member.EcmaId).ToArray());
            CollectionAssert.AreEqual(type.Interfaces.OrderBy(item => item, StringComparer.Ordinal).ToArray(), type.Interfaces.ToArray());
            AssertTypeUsesAreOrdinallyOrdered(type.Constraints);
            CollectionAssert.AreEqual(
                type.Attributes.OrderBy(item => item.Application, StringComparer.Ordinal).ThenBy(item => item.AttributeType, StringComparer.Ordinal)
                    .Select(AttributeKey).ToArray(),
                type.Attributes.Select(AttributeKey).ToArray());
            AssertNamedDocumentationIsOrdinallyOrdered(type.Documentation.Parameters);
            AssertNamedDocumentationIsOrdinallyOrdered(type.Documentation.TypeParameters);
            AssertNamedDocumentationIsOrdinallyOrdered(type.Documentation.Exceptions);
            foreach (var attribute in type.Attributes)
            {
                CollectionAssert.AreEqual(
                    attribute.ArgumentTypeNames.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    attribute.ArgumentTypeNames.ToArray());
            }
            foreach (var member in type.Members)
            {
                AssertTypeUsesAreOrdinallyOrdered(member.Constraints);
                AssertNamedDocumentationIsOrdinallyOrdered(member.Documentation.Parameters);
                AssertNamedDocumentationIsOrdinallyOrdered(member.Documentation.TypeParameters);
                AssertNamedDocumentationIsOrdinallyOrdered(member.Documentation.Exceptions);
                foreach (var typeUse in member.Parameters.Append(member.ReturnType).OfType<ApiTypeUse>())
                {
                    CollectionAssert.AreEqual(
                        typeUse.TypeNames.OrderBy(item => item, StringComparer.Ordinal).ToArray(), typeUse.TypeNames.ToArray());
                }
                foreach (var attribute in member.Attributes)
                {
                    CollectionAssert.AreEqual(
                        attribute.ArgumentTypeNames.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                        attribute.ArgumentTypeNames.ToArray());
                }
            }
        }
    }

    private static void AssertTypeUsesAreOrdinallyOrdered(IReadOnlyList<ApiTypeUse> items)
    {
        CollectionAssert.AreEqual(
            items.OrderBy(item => item.TypeExpression, StringComparer.Ordinal).Select(item => item.TypeExpression).ToArray(),
            items.Select(item => item.TypeExpression).ToArray());
        foreach (var item in items)
        {
            CollectionAssert.AreEqual(
                item.TypeNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(), item.TypeNames.ToArray());
        }
    }

    private static void AssertNamedDocumentationIsOrdinallyOrdered(IReadOnlyList<ApiNamedDocumentation> items) =>
        CollectionAssert.AreEqual(
            items.OrderBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.Text, StringComparer.Ordinal)
                .Select(item => item.Name + "\0" + item.Text).ToArray(),
            items.Select(item => item.Name + "\0" + item.Text).ToArray());

    private static string AttributeKey(ApiAttributeUse item) => item.Application + "\0" + item.AttributeType;

    private static string GetFixturePath(string key) => typeof(PackageApiCorpusBuilderTests).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value
        ?? throw new InvalidOperationException($"Missing test fixture metadata '{key}'.");
}
