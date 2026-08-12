using System.Reflection;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class MetadataApiReaderTests
{
    private static readonly string FixtureAssemblyPath = GetFixturePath("ApiFixtureAssemblyPath");
    private static readonly string[] ExpectedInterfaces = ["Fixtures.IGallery<T>"];
    private static readonly string[] ExpectedTypeConstraints =
        ["Fixtures.GalleryBase", "Fixtures.IMarker"];
    private static readonly string[] ExpectedAttributeArgumentTypes =
        ["Fixtures.Marker", "System.Uri"];
    private static readonly string[] ExpectedMarkerTypeNames = ["Fixtures.Marker"];
    private static readonly string[] ExpectedInt32TypeNames = ["System.Int32"];
    private static readonly string[] ExpectedUriTypeNames = ["System.Uri"];
    private static readonly string[] ExpectedStreamTypeNames = ["System.IO.Stream"];
    private static readonly string[] ExpectedAttributeTargetsTypeNames = ["System.AttributeTargets"];
    private static readonly string[] ExpectedNullableInterfaces =
        ["Fixtures.INullableMarker<System.Uri?>"];

    [TestMethod]
    public void ReadReturnsVisibleDeclarationsWithCSharpSignatures()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);

        Assert.AreEqual(1, corpus.SchemaVersion);
        var type = corpus.Types.Single(item => item.FullName == "Fixtures.SignatureGallery<T>");
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public ref readonly (string Name, T Value)? Borrow(in T value);"),
            string.Join(Environment.NewLine, type.Members.Select(item => item.Signature)));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public SignatureGallery(int capacity);"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public int Count { get; set; }"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public string PublicGetter { get; }"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public string Initial { get; init; }"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public string this[int index] { get; }"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public event System.EventHandler? Changed;"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public static readonly int[] Numbers;"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public System.Uri?[] Transform(Fixtures.Marker input, int* pointer, ref T current, in string label, out T result);"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public TResult Constrain<TResult>(TResult value) where TResult : System.IO.Stream, System.IDisposable, new();"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public TResult NullableConstrain<TResult>(TResult value) where TResult : Fixtures.INullableMarker<string?>;"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public TReference NullableClass<TReference>(TReference value) where TReference : class?;"));
        Assert.IsTrue(type.Members.Any(item =>
            item.Signature == "public void Collect(params string[] values);"));
        Assert.IsTrue(type.Members.Any(item => item.Name == "ProtectedOnly"));
        Assert.IsTrue(type.Members.Any(item => item.Name == "ProtectedInternalOnly"));
        Assert.IsFalse(type.Members.Any(item => item.Name == "InternalOnly"));
        Assert.IsFalse(type.Members.Any(item => item.Name == "PrivateOnly"));
        Assert.IsFalse(type.Members.Any(item => item.Name == "PrivateProtectedOnly"));
        var nested = corpus.Types.Single(item =>
            item.FullName == "Fixtures.SignatureGallery<T>.Nested<TNested>");
        Assert.AreEqual("Nested<TNested>", nested.Name);
        Assert.IsFalse(corpus.Types.Any(item => item.Name == "InternalNested"));
    }

    [TestMethod]
    public void ReadGeneratesStableEcmaDocumentationIds()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var type = corpus.Types.Single(item => item.FullName == "Fixtures.SignatureGallery<T>");

        Assert.AreEqual("T:Fixtures.SignatureGallery`1", type.EcmaId);
        Assert.AreEqual(
            "M:Fixtures.SignatureGallery`1.#ctor(System.Int32)",
            type.Members.Single(item => item.Kind == "constructor").EcmaId);
        Assert.AreEqual(
            "M:Fixtures.SignatureGallery`1.Borrow(`0@)",
            type.Members.Single(item => item.Name == "Borrow").EcmaId);
        Assert.AreEqual(
            "P:Fixtures.SignatureGallery`1.Item(System.Int32)",
            type.Members.Single(item => item.Kind == "indexer").EcmaId);
        Assert.AreEqual(
            "E:Fixtures.SignatureGallery`1.Changed",
            type.Members.Single(item => item.Kind == "event").EcmaId);
        Assert.AreEqual(
            "F:Fixtures.SignatureGallery`1.Numbers",
            type.Members.Single(item => item.Name == "Numbers").EcmaId);
        Assert.AreEqual(
            "M:Fixtures.SignatureGallery`1.op_Addition(Fixtures.SignatureGallery{`0},Fixtures.SignatureGallery{`0})",
            type.Members.Single(item => item.Kind == "operator").EcmaId);
    }

    [TestMethod]
    public void ReadCapturesCanonicalStructuralTypeUses()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var type = corpus.Types.Single(item => item.FullName == "Fixtures.SignatureGallery<T>");

        Assert.AreEqual("Fixtures.GalleryBase", type.BaseType);
        CollectionAssert.AreEqual(
            ExpectedInterfaces,
            type.Interfaces.ToArray());
        CollectionAssert.AreEquivalent(
            ExpectedTypeConstraints,
            type.Constraints.Select(item => item.TypeExpression).ToArray());

        var attribute = type.Attributes.Single(item =>
            item.AttributeType == "Fixtures.GalleryAttribute");
        Assert.AreEqual(
            "[Fixtures.Gallery(typeof(Fixtures.Marker), NamedType = typeof(System.Uri))]",
            attribute.Application);
        CollectionAssert.AreEquivalent(
            ExpectedAttributeArgumentTypes,
            attribute.ArgumentTypeNames.ToArray());

        var attributeType = corpus.Types.Single(item =>
            item.FullName == "Fixtures.GalleryAttribute");
        var usage = attributeType.Attributes.Single(item =>
            item.AttributeType == "System.AttributeUsageAttribute");
        Assert.AreEqual(
            "[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]",
            usage.Application);
        CollectionAssert.AreEqual(
            ExpectedAttributeTargetsTypeNames,
            usage.ArgumentTypeNames.ToArray());

        var transform = type.Members.Single(item => item.Name == "Transform");
        var input = transform.Parameters.Single(item => item.Name == "input");
        Assert.AreEqual("Fixtures.Marker", input.TypeExpression);
        CollectionAssert.AreEqual(ExpectedMarkerTypeNames, input.TypeNames.ToArray());
        var pointer = transform.Parameters.Single(item => item.Name == "pointer");
        Assert.AreEqual("int*", pointer.TypeExpression);
        CollectionAssert.AreEqual(ExpectedInt32TypeNames, pointer.TypeNames.ToArray());
        Assert.AreEqual("System.Uri?[]", transform.ReturnType!.TypeExpression);
        CollectionAssert.AreEqual(
            ExpectedUriTypeNames,
            transform.ReturnType.TypeNames.ToArray());

        var borrow = type.Members.Single(item => item.Name == "Borrow");
        Assert.AreEqual(
            "ref readonly (string Name, T Value)?",
            borrow.ReturnType!.TypeExpression);

        var constraint = type.Members.Single(item => item.Name == "Constrain")
            .Constraints.Single(item => item.TypeExpression == "System.IO.Stream");
        Assert.AreEqual("TResult", constraint.Name);
        CollectionAssert.AreEqual(
            ExpectedStreamTypeNames,
            constraint.TypeNames.ToArray());

        var obsolete = type.Members.Single(item => item.Name == "PublicOnly")
            .Attributes.Single(item => item.AttributeType == "System.ObsoleteAttribute");
        Assert.AreEqual("[System.Obsolete]", obsolete.Application);

        Assert.IsNull(type.Documentation.Summary);
        Assert.IsTrue(type.Documentation.Parameters.Count == 0);
    }

    [TestMethod]
    public void ReadPreservesNullabilityInStructuralTypePositions()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var type = corpus.Types.Single(item => item.FullName == "Fixtures.NullableShape<T>");

        Assert.AreEqual("Fixtures.NullableBase<string?>", type.BaseType);
        CollectionAssert.AreEqual(
            ExpectedNullableInterfaces,
            type.Interfaces.ToArray());
        Assert.AreEqual(
            "Fixtures.INullableMarker<string?>",
            type.Constraints.Single().TypeExpression);
    }

    [TestMethod]
    public void ReadRejectsAnUnknownExternalEnumWithItsIdentity()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMetadataString(bytes, "AttributeTargets", "DayOfWeek");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(exception.Message, "System.DayOfWeek");
    }

    [TestMethod]
    public void ReadRejectsAnUnsupportedSignatureModifierWithItsIdentity()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMetadataString(bytes, "InAttribute", "BadModifier");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(
            exception.Message,
            "System.Runtime.InteropServices.BadModifier");
    }

    [TestMethod]
    public void ReadRejectsACompoundSerializedSystemTypeArgument()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceFirstUtf8Occurrence(bytes, "System.Uri", "System.Ur*");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(exception.Message, "unsupported compound type form");
    }

    private static string GetFixturePath(string key) => typeof(MetadataApiReaderTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key)
        .Value!;

    private static void ReplaceMetadataString(byte[] bytes, string current, string replacement)
    {
        var currentBytes = System.Text.Encoding.UTF8.GetBytes(current);
        var replacementBytes = System.Text.Encoding.UTF8.GetBytes(replacement);
        Assert.IsTrue(replacementBytes.Length <= currentBytes.Length);
        var matches = Enumerable.Range(0, bytes.Length - currentBytes.Length + 1)
            .Where(index => bytes.AsSpan(index, currentBytes.Length).SequenceEqual(currentBytes))
            .ToArray();
        Assert.HasCount(1, matches, $"Expected one metadata string '{current}'.");

        var offset = matches[0];
        replacementBytes.CopyTo(bytes, offset);
        bytes.AsSpan(offset + replacementBytes.Length, currentBytes.Length - replacementBytes.Length).Clear();
    }

    private static void ReplaceFirstUtf8Occurrence(byte[] bytes, string current, string replacement)
    {
        var currentBytes = System.Text.Encoding.UTF8.GetBytes(current);
        var replacementBytes = System.Text.Encoding.UTF8.GetBytes(replacement);
        Assert.AreEqual(currentBytes.Length, replacementBytes.Length);
        var offset = Enumerable.Range(0, bytes.Length - currentBytes.Length + 1)
            .First(index => bytes.AsSpan(index, currentBytes.Length).SequenceEqual(currentBytes));
        replacementBytes.CopyTo(bytes, offset);
    }
}
