using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class MetadataApiReaderTests
{
    private static readonly string FixtureAssemblyPath = GetFixturePath("ApiFixtureAssemblyPath");
    private static readonly string[] ExpectedInterfaces = ["Fixtures.IGallery<T>"];
    private static readonly string[] ExpectedTypeConstraints =
        ["Fixtures.GalleryBase", "Fixtures.IMarker", "new()"];
    private static readonly string[] ExpectedAttributeArgumentTypes =
        ["Fixtures.Marker", "System.Uri"];
    private static readonly string[] ExpectedMarkerTypeNames = ["Fixtures.Marker"];
    private static readonly string[] ExpectedInt32TypeNames = ["System.Int32"];
    private static readonly string[] ExpectedStringTypeNames = ["System.String"];
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
            type.Members.Single(item => item.Name == "op_Addition").EcmaId);
    }

    [TestMethod]
    public void ReadDistributesConstructedNestedGenericArgumentsBySegment()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var member = corpus.Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Members.Single(item => item.Name == "UseNested");

        Assert.AreEqual(
            "public Fixtures.GenericOuter<string>.GenericInner<int> UseNested(Fixtures.GenericOuter<System.Uri>.GenericInner<long> value);",
            member.Signature);
        Assert.AreEqual(
            "M:Fixtures.SignatureGallery`1.UseNested(Fixtures.GenericOuter{System.Uri}.GenericInner{System.Int64})",
            member.EcmaId);
        Assert.AreEqual(
            "Fixtures.GenericOuter<string>.GenericInner<int>",
            member.ReturnType!.TypeExpression);
        Assert.AreEqual(
            "Fixtures.GenericOuter<System.Uri>.GenericInner<long>",
            member.Parameters.Single().TypeExpression);
    }

    [TestMethod]
    public void ReadRendersAllConversionAndIncrementOperatorForms()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var operators = corpus.Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Members.Where(item => item.Kind == "operator")
            .ToArray();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["public static implicit operator int(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_Implicit(Fixtures.SignatureGallery{`0})~System.Int32",
            ["public static explicit operator Fixtures.SignatureGallery<T>(int value);"] =
                "M:Fixtures.SignatureGallery`1.op_Explicit(System.Int32)~Fixtures.SignatureGallery{`0}",
            ["public static explicit operator byte(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_Explicit(Fixtures.SignatureGallery{`0})~System.Byte",
            ["public static explicit operator checked byte(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_CheckedExplicit(Fixtures.SignatureGallery{`0})~System.Byte",
            ["public static Fixtures.SignatureGallery<T> operator ++(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_Increment(Fixtures.SignatureGallery{`0})",
            ["public static Fixtures.SignatureGallery<T> operator checked ++(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_CheckedIncrement(Fixtures.SignatureGallery{`0})",
            ["public static Fixtures.SignatureGallery<T> operator --(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_Decrement(Fixtures.SignatureGallery{`0})",
            ["public static Fixtures.SignatureGallery<T> operator checked --(Fixtures.SignatureGallery<T> value);"] =
                "M:Fixtures.SignatureGallery`1.op_CheckedDecrement(Fixtures.SignatureGallery{`0})",
        };

        foreach (var pair in expected)
            Assert.AreEqual(pair.Value, operators.Single(item => item.Signature == pair.Key).EcmaId);
    }

    [TestMethod]
    public void ReadRetainsSpecialGenericConstraintsForTypesNestedTypesAndMethods()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);

        var constraintType = corpus.Types.Single(item =>
            item.FullName == "Fixtures.ConstraintGallery<TStruct, TUnmanaged, TClass, TNew>");
        Assert.AreEqual(
            "struct",
            string.Join(",", constraintType.Constraints.Where(item => item.Name == "TStruct")
                .Select(item => item.TypeExpression)));
        Assert.AreEqual(
            "unmanaged",
            string.Join(",", constraintType.Constraints.Where(item => item.Name == "TUnmanaged")
                .Select(item => item.TypeExpression)));
        Assert.AreEqual(
            "class",
            string.Join(",", constraintType.Constraints.Where(item => item.Name == "TClass")
                .Select(item => item.TypeExpression)));
        Assert.AreEqual(
            "class,new()",
            string.Join(",", constraintType.Constraints.Where(item => item.Name == "TNew")
                .Select(item => item.TypeExpression)));

        var signatureType = corpus.Types.Single(item =>
            item.FullName == "Fixtures.SignatureGallery<T>");
        Assert.IsTrue(signatureType.Constraints.Any(item =>
            item.Name == "T" && item.TypeExpression == "new()"));
        var nested = corpus.Types.Single(item =>
            item.FullName == "Fixtures.SignatureGallery<T>.Nested<TNested>");
        Assert.AreEqual(
            "unmanaged",
            string.Join(",", nested.Constraints.Where(item => item.Name == "TNested")
                .Select(item => item.TypeExpression)));

        var constrain = signatureType.Members.Single(item => item.Name == "Constrain");
        Assert.IsTrue(constrain.Constraints.Any(item =>
            item.Name == "TResult" && item.TypeExpression == "new()"));
        var nullableClass = signatureType.Members.Single(item => item.Name == "NullableClass");
        Assert.AreEqual(
            "class?",
            nullableClass.Constraints.Single(item => item.Name == "TReference").TypeExpression);
    }

    [TestMethod]
    public void ReadRendersPropertyAndEventAccessorSemantics()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var baseMembers = corpus.Types.Single(item => item.FullName == "Fixtures.AccessorBase").Members;
        var overrideMembers = corpus.Types.Single(item => item.FullName == "Fixtures.AccessorOverride").Members;

        Assert.IsTrue(baseMembers.Any(item =>
            item.Signature == "public abstract int AbstractProperty { get; }"));
        Assert.IsTrue(baseMembers.Any(item =>
            item.Signature == "public virtual int VirtualProperty { get; set; }"));
        Assert.IsTrue(baseMembers.Any(item =>
            item.Signature == "public abstract event System.EventHandler? AbstractEvent;"));
        Assert.IsTrue(baseMembers.Any(item =>
            item.Signature == "public virtual event System.EventHandler? VirtualEvent;"));

        Assert.IsTrue(overrideMembers.Any(item =>
            item.Signature == "public override int AbstractProperty { get; }"));
        Assert.IsTrue(overrideMembers.Any(item =>
            item.Signature == "public sealed override int VirtualProperty { get; set; }"));
        Assert.IsTrue(overrideMembers.Any(item =>
            item.Signature == "public override event System.EventHandler? AbstractEvent;"));
        Assert.IsTrue(overrideMembers.Any(item =>
            item.Signature == "public sealed override event System.EventHandler? VirtualEvent;"));

        var staticMembers = corpus.Types.Single(item =>
            item.FullName == "Fixtures.IStaticAccessors").Members;
        Assert.IsTrue(staticMembers.Any(item =>
            item.Signature == "public static abstract int AbstractProperty { get; }"));
        Assert.IsTrue(staticMembers.Any(item =>
            item.Signature == "public static virtual int VirtualProperty { get; }"));
        Assert.IsTrue(staticMembers.Any(item =>
            item.Signature == "public static abstract event System.EventHandler? AbstractEvent;"));
        Assert.IsTrue(staticMembers.Any(item =>
            item.Signature == "public static virtual event System.EventHandler? VirtualEvent;"));
    }

    [TestMethod]
    public void ReadRejectsAGetterSignatureThatDisagreesWithItsProperty()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignature(bytes, "get_AccessorProbe", "get_AccessorSource");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(exception.Message, "get_AccessorProbe");
    }

    [TestMethod]
    public void ReadRejectsAnEventAccessorSignatureThatDisagreesWithItsEvent()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignature(bytes, "add_AbstractEvent", "add_OtherEvent");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(exception.Message, "add_AbstractEvent");
    }

    [TestMethod]
    public void ReadRejectsAVisibleOtherAccessorInsteadOfEmittingAnEmptyProperty()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeGetterSemanticsToOther(bytes, "AccessorProbe");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));

        StringAssert.Contains(exception.Message, "get_AccessorProbe");
    }

    [TestMethod]
    public void ReadDistinguishesSzArraysFromRankOneNonSzArrays()
    {
        using (var stream = File.OpenRead(FixtureAssemblyPath))
        {
            var member = MetadataApiReader.Read(stream).Types
                .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
                .Members.Single(item => item.Name == "ArrayShapeProbe");
            Assert.AreEqual(
                "public int[] ArrayShapeProbe(int[] values, string marker);",
                member.Signature);
            Assert.AreEqual(
                "M:Fixtures.SignatureGallery`1.ArrayShapeProbe(System.Int32[],System.String)",
                member.EcmaId);
        }

        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureBytes(
            bytes,
            "SignatureGallery`1",
            "ArrayShapeProbe",
            [0x20, 0x00, 0x14, 0x08, 0x01, 0x00, 0x00]);
        using var corruptedStream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(corruptedStream));
        StringAssert.Contains(exception.Message, "non-SZ");
    }

    [TestMethod]
    public void ReadRendersRepresentableMultidimensionalArraysExactly()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var corpus = MetadataApiReader.Read(stream);
        var member = corpus.Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Members.Single(item => item.Name == "MultiDimensionalArrayProbe");

        Assert.AreEqual(
            "public int[,] MultiDimensionalArrayProbe(int[,] matrix, string[,,] cube);",
            member.Signature);
        Assert.AreEqual(
            "M:Fixtures.SignatureGallery`1.MultiDimensionalArrayProbe(System.Int32[0:,0:],System.String[0:,0:,0:])",
            member.EcmaId);
        Assert.AreEqual("int[,]", member.ReturnType!.TypeExpression);
        CollectionAssert.AreEqual(
            ExpectedInt32TypeNames,
            member.ReturnType.TypeNames.ToArray());
        Assert.AreEqual("int[,]", member.Parameters[0].TypeExpression);
        CollectionAssert.AreEqual(
            ExpectedInt32TypeNames,
            member.Parameters[0].TypeNames.ToArray());
        Assert.AreEqual("string[,,]", member.Parameters[1].TypeExpression);
        CollectionAssert.AreEqual(
            ExpectedStringTypeNames,
            member.Parameters[1].TypeNames.ToArray());

        var rankThirtyTwo = corpus.Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Members.Single(item => item.Name == "RankThirtyTwoArrayProbe");
        const string rankThirtyTwoCSharp = "int[,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,]";
        const string rankThirtyTwoEcma =
            "System.Int32[0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,"
            + "0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:,0:]";
        Assert.AreEqual(
            $"public {rankThirtyTwoCSharp} RankThirtyTwoArrayProbe({rankThirtyTwoCSharp} values);",
            rankThirtyTwo.Signature);
        Assert.AreEqual(
            $"M:Fixtures.SignatureGallery`1.RankThirtyTwoArrayProbe({rankThirtyTwoEcma})",
            rankThirtyTwo.EcmaId);
    }

    [TestMethod]
    public void ReadRejectsMultidimensionalArraysWithNonRepresentableShapeData()
    {
        var mutations = new[]
        {
            (Signature: new byte[] { 0x20, 0x00, 0x14, 0x08, 0x02, 0x01, 0x04, 0x00 },
                Expected: "sizes"),
            (Signature: new byte[] { 0x20, 0x00, 0x14, 0x08, 0x02, 0x00, 0x02, 0x00, 0x02 },
                Expected: "lower bounds"),
            (Signature: new byte[] { 0x20, 0x00, 0x14, 0x08, 0x21, 0x00, 0x00 },
                Expected: "rank"),
        };

        foreach (var mutation in mutations)
        {
            var bytes = File.ReadAllBytes(FixtureAssemblyPath);
            ReplaceMethodSignatureBytes(
                bytes,
                "SignatureGallery`1",
                "MultiDimensionalArrayProbe",
                mutation.Signature);

            using var stream = new MemoryStream(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, mutation.Expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void ReadRejectsAMultidimensionalArrayWithTooFewZeroLowerBounds()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureBytes(
            bytes,
            "SignatureGallery`1",
            "MultiDimensionalArrayProbe",
            [0x20, 0x00, 0x14, 0x08, 0x02, 0x00, 0x01, 0x00]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "lower bounds", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ReadRejectsAMultidimensionalArrayWithTooManyZeroLowerBounds()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureBytes(
            bytes,
            "SignatureGallery`1",
            "MultiDimensionalArrayProbe",
            [0x20, 0x00, 0x14, 0x08, 0x02, 0x00, 0x03, 0x00, 0x00, 0x00]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "lower bounds", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ReadRejectsAByReferenceTypeNestedInsideAnArray()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureBytes(
            bytes,
            "SignatureGallery`1",
            "NestedByRefProbe",
            [0x20, 0x02, 0x1D, 0x10, 0x08, 0x08, 0x0E]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "by-reference");
    }

    [TestMethod]
    public void ReadRejectsAConstructorWhoseMetadataReturnTypeIsNotVoid()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodReturnType(bytes, "ConstructorProbe", ".ctor", 0x01, 0x08);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "ConstructorProbe");
    }

    [TestMethod]
    public void ReadAppliesFlattenedNestedTupleNamesOuterFirst()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var member = MetadataApiReader.Read(stream).Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Members.Single(item => item.Name == "NestedTuple");

        Assert.AreEqual(
            "public ((string First, string Second) Pair, string Tail) NestedTuple(((string First, string Second) Pair, string Tail) value);",
            member.Signature);
    }

    [TestMethod]
    public void ReadRejectsAShortTupleNameTransform()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        RewriteReturnTransform(bytes, "SignatureGallery`1", "NestedTuple", "TupleElementNamesAttribute", ["A", "B", "C"]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "TupleElementNamesAttribute");
    }

    [TestMethod]
    public void ReadRejectsAnExtraTupleNameTransform()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        RewriteReturnTransform(bytes, "SignatureGallery`1", "NestedTuple", "TupleElementNamesAttribute", ["A", "B", "C", "D", "E"]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "TupleElementNamesAttribute");
    }

    [TestMethod]
    public void ReadRejectsAShortNullableTransform()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        RewriteReturnNullableFlags(bytes, "SignatureGallery`1", "NullableTransformProbe", [1]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "NullableAttribute");
    }

    [TestMethod]
    public void ReadRejectsAnExtraNullableTransform()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureWith(
            bytes,
            "SignatureGallery`1",
            "NullableTransformProbe",
            "NullableTransformSource");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "NullableAttribute");
    }

    [TestMethod]
    public void ReadRejectsAnInvalidNullableTransformFlag()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        RewriteReturnNullableFlags(bytes, "SignatureGallery`1", "NullableTransformProbe", [1, 3]);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "flag '3'");
    }

    [TestMethod]
    public void ReadRendersLocalEnumAndGenericAttributesWithoutLoadingDependencies()
    {
        using var stream = File.OpenRead(FixtureAssemblyPath);
        var attributes = MetadataApiReader.Read(stream).Types
            .Single(item => item.FullName == "Fixtures.SignatureGallery<T>")
            .Attributes;

        var options = attributes.Single(item =>
            item.AttributeType == "Fixtures.OptionsAttribute");
        Assert.AreEqual(
            "[Fixtures.Options(Fixtures.FixtureOptions.First | Fixtures.FixtureOptions.Second)]",
            options.Application);
        CollectionAssert.Contains(options.ArgumentTypeNames.ToArray(), "Fixtures.FixtureOptions");

        var generic = attributes.Single(item =>
            item.AttributeType == "Fixtures.GenericTagAttribute`1");
        Assert.AreEqual("[Fixtures.GenericTag<int>]", generic.Application);
    }

    [TestMethod]
    public void ReadInheritsNullableContextFromTheDeclaringType()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReparentFieldNullableAttributes(bytes, "Nested", "Value");
        ReparentNullableContext(bytes, "DeclaringContextProbe");

        using var stream = new MemoryStream(bytes);
        var nested = MetadataApiReader.Read(stream).Types.Single(item =>
            item.FullName == "Fixtures.DeclaringContextProbe.Nested");
        Assert.IsTrue(nested.Members.Any(item => item.Signature == "public string? Value;"));
    }

    [TestMethod]
    public void ReadInheritsNullableContextFromTheModule()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReparentFieldNullableAttributes(bytes, "ModuleContextProbe", "Value");
        ReparentNullableContext(bytes, targetTypeName: null);

        using var stream = new MemoryStream(bytes);
        var type = MetadataApiReader.Read(stream).Types.Single(item =>
            item.FullName == "Fixtures.ModuleContextProbe");
        Assert.IsTrue(type.Members.Any(item => item.Signature == "public string? Value;"));
    }

    [TestMethod]
    public void ReadRejectsAConstructedTypeWhoseArgumentCountDoesNotMatchSegmentArities()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeFirstGenericArgumentCount(bytes, "SignatureGallery`1", "UseNested", 2, 1);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "declared generic arguments");
    }

    [TestMethod]
    public void ReadRejectsDuplicateEcmaDocumentationIds()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMetadataString(bytes, "DuplicateB", "DuplicateA");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(
            exception.Message,
            "M:Fixtures.SignatureGallery`1.DuplicateA(System.Int32)");
    }

    [TestMethod]
    public void ReadRejectsMalformedConstructorMetadataShapes()
    {
        var mutations = new (string Current, string Replacement)[]
        {
            ("Xctor", ".ctor"),
            ("Xcctor", ".cctor"),
            ("Ycctor", ".cctor"),
            ("Zctor", ".ctor"),
        };
        foreach (var mutation in mutations)
        {
            var bytes = File.ReadAllBytes(FixtureAssemblyPath);
            ReplaceMetadataString(bytes, mutation.Current, mutation.Replacement);

            using var stream = new MemoryStream(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, "constructor", StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void ReadRejectsUnknownInstanceAndWrongArityOperators()
    {
        var mutations = new (string Current, string Replacement)[]
        {
            ("BogusApi", "op_Bogus"),
            ("IncrementApi", "op_Increment"),
            ("AdditionApi", "op_Addition"),
            ("ExplicitApi", "op_Explicit"),
        };
        foreach (var mutation in mutations)
        {
            var bytes = File.ReadAllBytes(FixtureAssemblyPath);
            ReplaceMetadataString(bytes, mutation.Current, mutation.Replacement);

            using var stream = new MemoryStream(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, mutation.Replacement);
        }
    }

    [TestMethod]
    public void ReadRejectsAnAccessorWhoseClassValueTypeShapeDisagrees()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeSignatureElementType(bytes, "SignatureGallery`1", "get_AccessorProbe", 0x12, 0x11);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "get_AccessorProbe");
    }

    [TestMethod]
    public void ReadRejectsStaticVirtualAccessorsOnANonInterfaceType()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeTypeAttributes(
            bytes,
            "IStaticAccessors",
            attributes => attributes & ~TypeAttributes.Interface);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "interface", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("SignatureGallery`1", "get_AccessorProbe", (byte)0x25)]
    [DataRow("AccessorBase", "add_AbstractEvent", (byte)0x60)]
    public void ReadRejectsUnsupportedAccessorCallingConventions(
        string typeName,
        string methodName,
        byte replacementHeader)
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodSignatureHeader(bytes, typeName, methodName, 0x20, replacementHeader);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, methodName);
        StringAssert.Contains(exception.Message, "calling convention", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ReadRejectsAPropertyWhoseStaticnessDisagreesWithItsAccessor()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangePropertySignatureHeader(bytes, "SignatureGallery`1", "AccessorProbe", 0x28, 0x08);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "AccessorProbe");
        StringAssert.Contains(exception.Message, "static", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ReadRejectsEventAccessorsWithIncompatibleStaticness()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodAttributes(
            bytes,
            "AccessorBase",
            "add_AbstractEvent",
            attributes => attributes | MethodAttributes.Static);
        ChangeMethodSignatureHeader(bytes, "AccessorBase", "add_AbstractEvent", 0x20, 0x00);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "AbstractEvent");
        StringAssert.Contains(exception.Message, "incompatible", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void ReadRejectsExtraNullableFlagsOnAClassConstraint()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceGenericParameterNullableTransformWithArray(
            bytes,
            "SignatureGallery`1",
            "NullableClass");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "NullableAttribute");
    }

    [TestMethod]
    public void ReadRejectsConstructorIncompatibleFlagsAndStaticConstructorAccessibility()
    {
        var virtualConstructor = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodAttributes(
            virtualConstructor,
            "SignatureGallery`1",
            ".ctor",
            attributes => attributes | MethodAttributes.Virtual);
        using (var stream = new MemoryStream(virtualConstructor))
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, "constructor", StringComparison.OrdinalIgnoreCase);
        }

        var publicTypeInitializer = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodAttributes(
            publicTypeInitializer,
            "SignatureGallery`1",
            ".cctor",
            attributes => attributes & ~MethodAttributes.MemberAccessMask | MethodAttributes.Public);
        using (var stream = new MemoryStream(publicTypeInitializer))
        {
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, "initializer", StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void ReadRejectsWrappedAndByReferenceOperatorOwnership()
    {
        foreach (var sourceName in new[] { "WrappedOperatorSource", "RefOperatorSource" })
        {
            var bytes = File.ReadAllBytes(FixtureAssemblyPath);
            ReplaceMethodSignatureWith(
                bytes,
                "SignatureGallery`1",
                "op_Addition",
                sourceName);
            using var stream = new MemoryStream(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, "op_Addition");
        }
    }

    [TestMethod]
    public void ReadRejectsInvalidIncrementAndBooleanOperatorReturnTypes()
    {
        foreach (var operatorName in new[] { "op_Increment", "op_True" })
        {
            var bytes = File.ReadAllBytes(FixtureAssemblyPath);
            ReplaceMethodSignatureWith(
                bytes,
                "SignatureGallery`1",
                operatorName,
                "IncrementReturnSource");
            using var stream = new MemoryStream(bytes);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
                MetadataApiReader.Read(stream));
            StringAssert.Contains(exception.Message, operatorName);
        }
    }

    [TestMethod]
    public void ReadRejectsAnOperatorUsingTheWrongConstructedDeclaringType()
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ReplaceMethodSignatureWith(
            bytes,
            "SignatureGallery`1",
            "op_Increment",
            "WrongConstructedOperatorSource");

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, "op_Increment");
    }

    [TestMethod]
    [DataRow("RefParameterOperatorSource", "op_Addition")]
    [DataRow("RefConversionReturnSource", "op_Implicit")]
    [DataRow("RefConversionReturnSource", "op_CheckedExplicit")]
    public void ReadRejectsByReferenceOperatorParametersAndReturns(
        string sourceName,
        string operatorName)
    {
        var bytes = File.ReadAllBytes(FixtureAssemblyPath);
        ChangeMethodAttributes(
            bytes,
            "SignatureGallery`1",
            sourceName,
            attributes => attributes | MethodAttributes.SpecialName);
        ReplaceMetadataString(bytes, sourceName, operatorName);

        using var stream = new MemoryStream(bytes);
        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            MetadataApiReader.Read(stream));
        StringAssert.Contains(exception.Message, operatorName);
        StringAssert.Contains(exception.Message, "by-reference", StringComparison.OrdinalIgnoreCase);
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

    private static void ReplaceMethodSignature(byte[] bytes, string targetName, string sourceName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var target = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .First(method => metadata.GetString(method.Name) == targetName);
        var source = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(method => metadata.GetString(method.Name) == sourceName);
        var targetBytes = metadata.GetBlobBytes(target.Signature);
        var sourceBytes = metadata.GetBlobBytes(source.Signature);
        Assert.AreEqual(targetBytes.Length, sourceBytes.Length, "Mutation must preserve blob size.");

        var heapStart = GetMetadataStreamFileOffset(bytes, peReader.PEHeaders.MetadataStartOffset, "#Blob");
        var blobOffset = MetadataTokens.GetHeapOffset(target.Signature);
        var prefixSize = GetCompressedIntegerSize(bytes[heapStart + blobOffset]);
        sourceBytes.CopyTo(bytes, heapStart + blobOffset + prefixSize);
    }

    private static void ChangeGetterSemanticsToOther(byte[] bytes, string propertyName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var propertyHandle = metadata.PropertyDefinitions.Single(handle =>
            metadata.GetString(metadata.GetPropertyDefinition(handle).Name) == propertyName);
        var getterHandle = metadata.GetPropertyDefinition(propertyHandle).GetAccessors().Getter;
        Assert.IsFalse(getterHandle.IsNil);
        Assert.IsTrue(metadata.MethodDefinitions.Count < ushort.MaxValue);
        Assert.IsTrue(metadata.PropertyDefinitions.Count * 2 + 1 < ushort.MaxValue);

        var row = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(row, (ushort)MethodSemanticsAttributes.Getter);
        BinaryPrimitives.WriteUInt16LittleEndian(
            row.AsSpan(2),
            checked((ushort)MetadataTokens.GetRowNumber(getterHandle)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            row.AsSpan(4),
            checked((ushort)((MetadataTokens.GetRowNumber(propertyHandle) << 1) | 1)));
        var matches = Enumerable.Range(0, bytes.Length - row.Length + 1)
            .Where(index => bytes.AsSpan(index, row.Length).SequenceEqual(row))
            .ToArray();
        Assert.HasCount(1, matches, "Expected one MethodSemantics row for the getter.");
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(matches[0], sizeof(ushort)),
            (ushort)MethodSemanticsAttributes.Other);
    }

    private static void ReplaceMethodSignatureBytes(
        byte[] bytes,
        string typeName,
        string methodName,
        byte[] replacement)
    {
        var location = GetMethodSignatureLocation(bytes, typeName, methodName);
        Assert.IsTrue(replacement.Length <= location.Length);
        Assert.IsTrue(replacement.Length < 0x80);
        Assert.AreEqual(1, location.PrefixSize);
        bytes[location.PrefixOffset] = checked((byte)replacement.Length);
        replacement.CopyTo(bytes, location.DataOffset);
        bytes.AsSpan(location.DataOffset + replacement.Length, location.Length - replacement.Length).Clear();
    }

    private static void ChangeMethodReturnType(
        byte[] bytes,
        string typeName,
        string methodName,
        byte oldType,
        byte newType)
    {
        var location = GetMethodSignatureLocation(bytes, typeName, methodName);
        var signature = bytes.AsSpan(location.DataOffset, location.Length);
        Assert.IsGreaterThanOrEqualTo(3, signature.Length);
        Assert.AreEqual(oldType, signature[2]);
        signature[2] = newType;
    }

    private static void ChangeMethodSignatureHeader(
        byte[] bytes,
        string typeName,
        string methodName,
        byte expectedHeader,
        byte replacementHeader)
    {
        var location = GetMethodSignatureLocation(bytes, typeName, methodName);
        Assert.AreEqual(expectedHeader, bytes[location.DataOffset]);
        bytes[location.DataOffset] = replacementHeader;
    }

    private static void ChangePropertySignatureHeader(
        byte[] bytes,
        string typeName,
        string propertyName,
        byte expectedHeader,
        byte replacementHeader)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var property = metadata.PropertyDefinitions
            .Select(metadata.GetPropertyDefinition)
            .Single(item => metadata.GetString(item.Name) == propertyName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        var heapStart = GetMetadataStreamFileOffset(bytes, peReader.PEHeaders.MetadataStartOffset, "#Blob");
        var blobOffset = MetadataTokens.GetHeapOffset(property.Signature);
        var prefixOffset = heapStart + blobOffset;
        var prefixSize = GetCompressedIntegerSize(bytes[prefixOffset]);
        Assert.AreEqual(expectedHeader, bytes[prefixOffset + prefixSize]);
        bytes[prefixOffset + prefixSize] = replacementHeader;
    }

    private static SignatureLocation GetMethodSignatureLocation(
        byte[] bytes,
        string typeName,
        string methodName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var method = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(item => metadata.GetString(item.Name) == methodName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        var heapStart = GetMetadataStreamFileOffset(bytes, peReader.PEHeaders.MetadataStartOffset, "#Blob");
        var blobOffset = MetadataTokens.GetHeapOffset(method.Signature);
        var prefixOffset = heapStart + blobOffset;
        var prefixSize = GetCompressedIntegerSize(bytes[prefixOffset]);
        return new SignatureLocation(
            prefixOffset,
            prefixOffset + prefixSize,
            metadata.GetBlobBytes(method.Signature).Length,
            prefixSize);
    }

    private static void RewriteReturnTransform(
        byte[] bytes,
        string typeName,
        string methodName,
        string attributeName,
        IReadOnlyList<string?> values)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var method = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(item => metadata.GetString(item.Name) == methodName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        var returnParameter = method.GetParameters()
            .Select(metadata.GetParameter)
            .Single(item => item.SequenceNumber == 0);
        var attribute = returnParameter.GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Single(item => GetAttributeTypeName(metadata, item) == attributeName);
        var replacement = SerializeStringArrayAttribute(values);
        RewriteBlob(bytes, peReader, metadata, attribute.Value, replacement);
    }

    private static void RewriteReturnNullableFlags(
        byte[] bytes,
        string typeName,
        string methodName,
        IReadOnlyList<byte> flags)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var method = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(item => metadata.GetString(item.Name) == methodName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        var returnParameter = method.GetParameters()
            .Select(metadata.GetParameter)
            .Single(item => item.SequenceNumber == 0);
        var attribute = returnParameter.GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Single(item => GetAttributeTypeName(metadata, item) == "NullableAttribute");
        RewriteBlob(bytes, peReader, metadata, attribute.Value, SerializeByteArrayAttribute(flags));
    }

    private static void ReplaceMethodSignatureWith(
        byte[] bytes,
        string typeName,
        string targetMethodName,
        string sourceMethodName)
    {
        var sourceLocation = GetMethodSignatureLocation(bytes, typeName, sourceMethodName);
        var sourceSignature = bytes.AsSpan(sourceLocation.DataOffset, sourceLocation.Length).ToArray();
        ReplaceMethodSignatureBytes(bytes, typeName, targetMethodName, sourceSignature);
    }

    private static string GetAttributeTypeName(MetadataReader metadata, CustomAttribute attribute)
    {
        var declaringType = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => metadata.GetMethodDefinition(
                (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            HandleKind.MemberReference => metadata.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor).Parent,
            _ => throw new AssertFailedException("Unsupported attribute constructor handle."),
        };
        return declaringType.Kind switch
        {
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition(
                (TypeDefinitionHandle)declaringType).Name),
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference(
                (TypeReferenceHandle)declaringType).Name),
            _ => throw new AssertFailedException("Unsupported attribute declaring type handle."),
        };
    }

    private static byte[] SerializeStringArrayAttribute(IReadOnlyList<string?> values)
    {
        var result = new List<byte> { 0x01, 0x00 };
        var count = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(count, values.Count);
        result.AddRange(count);
        foreach (var value in values)
        {
            if (value is null)
            {
                result.Add(0xFF);
                continue;
            }
            var encoded = Encoding.UTF8.GetBytes(value);
            Assert.IsLessThan(0x80, encoded.Length);
            result.Add(checked((byte)encoded.Length));
            result.AddRange(encoded);
        }
        result.Add(0x00);
        result.Add(0x00);
        return result.ToArray();
    }

    private static byte[] SerializeByteArrayAttribute(IReadOnlyList<byte> values)
    {
        var result = new byte[2 + sizeof(int) + values.Count + 2];
        result[0] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), values.Count);
        for (var index = 0; index < values.Count; index++)
            result[2 + sizeof(int) + index] = values[index];
        return result;
    }

    private static void RewriteBlob(
        byte[] bytes,
        PEReader peReader,
        MetadataReader metadata,
        BlobHandle handle,
        byte[] replacement)
    {
        var heapStart = GetMetadataStreamFileOffset(bytes, peReader.PEHeaders.MetadataStartOffset, "#Blob");
        var blobOffset = MetadataTokens.GetHeapOffset(handle);
        var prefixOffset = heapStart + blobOffset;
        var prefixSize = GetCompressedIntegerSize(bytes[prefixOffset]);
        var originalLength = metadata.GetBlobBytes(handle).Length;
        Assert.AreEqual(1, prefixSize);
        Assert.IsTrue(replacement.Length <= originalLength);
        Assert.IsLessThan(0x80, replacement.Length);
        bytes[prefixOffset] = checked((byte)replacement.Length);
        replacement.CopyTo(bytes, prefixOffset + prefixSize);
        bytes.AsSpan(prefixOffset + prefixSize + replacement.Length, originalLength - replacement.Length).Clear();
    }

    private static void ReparentNullableContext(byte[] bytes, string? targetTypeName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        byte[] nullableContextBlob = [0x01, 0x00, 0x02, 0x00, 0x00];
        var attribute = metadata.CustomAttributes
            .Select(metadata.GetCustomAttribute)
            .First(item => GetAttributeTypeName(metadata, item) == "NullableContextAttribute"
                && metadata.GetBlobBytes(item.Value).AsSpan().SequenceEqual(nullableContextBlob));
        Assert.IsTrue(metadata.CustomAttributes.Count < ushort.MaxValue);
        Assert.IsTrue(metadata.MethodDefinitions.Count * 8 + 3 < ushort.MaxValue);
        Assert.IsTrue(metadata.MemberReferences.Count * 8 + 3 < ushort.MaxValue);

        var row = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(row, EncodeHasCustomAttribute(attribute.Parent));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(2), EncodeCustomAttributeType(attribute.Constructor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            row.AsSpan(4),
            checked((ushort)MetadataTokens.GetHeapOffset(attribute.Value)));
        var matches = Enumerable.Range(0, bytes.Length - row.Length + 1)
            .Where(index => bytes.AsSpan(index, row.Length).SequenceEqual(row))
            .ToArray();
        Assert.HasCount(1, matches, "Expected one CustomAttribute row for nullable context.");

        var newParent = targetTypeName is null
            ? checked((ushort)((1 << 5) | 7))
            : checked((ushort)((MetadataTokens.GetRowNumber(metadata.TypeDefinitions.Single(handle =>
                metadata.GetString(metadata.GetTypeDefinition(handle).Name) == targetTypeName)) << 5) | 3));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(matches[0], sizeof(ushort)), newParent);
        SortCustomAttributeTable(bytes);
    }

    private static ushort EncodeHasCustomAttribute(EntityHandle handle)
    {
        var tag = handle.Kind switch
        {
            HandleKind.MethodDefinition => 0,
            HandleKind.FieldDefinition => 1,
            HandleKind.TypeReference => 2,
            HandleKind.TypeDefinition => 3,
            HandleKind.Parameter => 4,
            HandleKind.InterfaceImplementation => 5,
            HandleKind.MemberReference => 6,
            HandleKind.ModuleDefinition => 7,
            HandleKind.PropertyDefinition => 9,
            HandleKind.EventDefinition => 10,
            HandleKind.AssemblyDefinition => 14,
            HandleKind.GenericParameter => 19,
            HandleKind.GenericParameterConstraint => 20,
            _ => throw new AssertFailedException($"Unsupported custom attribute parent '{handle.Kind}'."),
        };
        return checked((ushort)((MetadataTokens.GetRowNumber(handle) << 5) | tag));
    }

    private static ushort EncodeCustomAttributeType(EntityHandle handle)
    {
        var tag = handle.Kind switch
        {
            HandleKind.MethodDefinition => 2,
            HandleKind.MemberReference => 3,
            _ => throw new AssertFailedException($"Unsupported custom attribute constructor '{handle.Kind}'."),
        };
        return checked((ushort)((MetadataTokens.GetRowNumber(handle) << 3) | tag));
    }

    private static void ReparentFieldNullableAttributes(
        byte[] bytes,
        string typeName,
        string fieldName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var type = metadata.TypeDefinitions.Single(handle =>
            metadata.GetString(metadata.GetTypeDefinition(handle).Name) == typeName);
        var field = metadata.GetTypeDefinition(type).GetFields()
            .Select(metadata.GetFieldDefinition)
            .Single(item => metadata.GetString(item.Name) == fieldName);
        var attributes = field.GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Where(item => GetAttributeTypeName(metadata, item) == "NullableAttribute")
            .ToArray();
        foreach (var attribute in attributes)
            RewriteCustomAttributeParent(bytes, attribute, checked((ushort)((1 << 5) | 14)));
    }

    private static void RewriteCustomAttributeParent(
        byte[] bytes,
        CustomAttribute attribute,
        ushort newParent)
    {
        var row = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(row, EncodeHasCustomAttribute(attribute.Parent));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(2), EncodeCustomAttributeType(attribute.Constructor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            row.AsSpan(4),
            checked((ushort)MetadataTokens.GetHeapOffset(attribute.Value)));
        var matches = Enumerable.Range(0, bytes.Length - row.Length + 1)
            .Where(index => bytes.AsSpan(index, row.Length).SequenceEqual(row))
            .ToArray();
        Assert.HasCount(1, matches);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(matches[0], sizeof(ushort)), newParent);
        SortCustomAttributeTable(bytes);
    }

    private static void SortCustomAttributeTable(byte[] bytes)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var tableStream = GetMetadataStreamFileOffset(
            bytes,
            peReader.PEHeaders.MetadataStartOffset,
            "#~");
        var heapSizes = bytes[tableStream + 6];
        var valid = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(tableStream + 8));
        var rowCounts = new int[64];
        var position = tableStream + 24;
        for (var table = 0; table < rowCounts.Length; table++)
        {
            if ((valid & (1UL << table)) == 0)
                continue;
            rowCounts[table] = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(position, sizeof(int)));
            position += sizeof(int);
        }

        var stringIndex = (heapSizes & 0x01) == 0 ? 2 : 4;
        var guidIndex = (heapSizes & 0x02) == 0 ? 2 : 4;
        var blobIndex = (heapSizes & 0x04) == 0 ? 2 : 4;
        var tableStart = position;
        var precedingSize =
            rowCounts[0] * (2 + stringIndex + guidIndex * 3)
            + rowCounts[1] * (CodedIndexSize(rowCounts, 2, 0, 1, 26, 35) + stringIndex * 2)
            + rowCounts[2] * (4 + stringIndex * 2 + CodedIndexSize(rowCounts, 2, 2, 1, 27)
                + TableIndexSize(rowCounts[4]) + TableIndexSize(rowCounts[6]))
            + rowCounts[3] * TableIndexSize(rowCounts[4])
            + rowCounts[4] * (2 + stringIndex + blobIndex)
            + rowCounts[5] * TableIndexSize(rowCounts[6])
            + rowCounts[6] * (8 + stringIndex + blobIndex + TableIndexSize(rowCounts[8]))
            + rowCounts[7] * TableIndexSize(rowCounts[8])
            + rowCounts[8] * (4 + stringIndex)
            + rowCounts[9] * (TableIndexSize(rowCounts[2]) + CodedIndexSize(rowCounts, 2, 2, 1, 27))
            + rowCounts[10] * (CodedIndexSize(rowCounts, 3, 2, 1, 26, 6, 27) + stringIndex + blobIndex)
            + rowCounts[11] * (2 + CodedIndexSize(rowCounts, 2, 4, 8, 23) + blobIndex);
        var rowSize = CodedIndexSize(
                rowCounts,
                5,
                6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43)
            + CodedIndexSize(rowCounts, 3, 6, 10)
            + blobIndex;
        Assert.AreEqual(6, rowSize, "Fixture mutation assumes two-byte custom-attribute indexes.");
        var tableOffset = tableStart + precedingSize;
        var rows = Enumerable.Range(0, rowCounts[12])
            .Select(index => bytes.AsSpan(tableOffset + index * rowSize, rowSize).ToArray())
            .OrderBy(row => BinaryPrimitives.ReadUInt16LittleEndian(row))
            .ToArray();
        for (var index = 0; index < rows.Length; index++)
            rows[index].CopyTo(bytes, tableOffset + index * rowSize);
    }

    private static int TableIndexSize(int rowCount) => rowCount < ushort.MaxValue ? 2 : 4;

    private static int CodedIndexSize(
        int[] rowCounts,
        int tagBits,
        params int[] tables) => tables.Max(table => rowCounts[table]) < (1 << (16 - tagBits))
            ? 2
            : 4;

    private static void ChangeFirstGenericArgumentCount(
        byte[] bytes,
        string typeName,
        string methodName,
        byte oldCount,
        byte newCount)
    {
        var location = GetMethodSignatureLocation(bytes, typeName, methodName);
        var signature = bytes.AsSpan(location.DataOffset, location.Length);
        for (var index = 0; index < signature.Length - 4; index++)
        {
            if (signature[index] != 0x15 || signature[index + 1] is not (0x11 or 0x12))
                continue;
            var tokenSize = GetCompressedIntegerSize(signature[index + 2]);
            var countIndex = index + 2 + tokenSize;
            if (signature[countIndex] != oldCount)
                continue;
            signature[countIndex] = newCount;
            return;
        }
        Assert.Fail($"No generic instantiation with {oldCount} arguments was found.");
    }

    private static void ChangeSignatureElementType(
        byte[] bytes,
        string typeName,
        string methodName,
        byte oldElementType,
        byte newElementType)
    {
        var location = GetMethodSignatureLocation(bytes, typeName, methodName);
        var signature = bytes.AsSpan(location.DataOffset, location.Length);
        var matches = new List<int>();
        for (var index = 0; index < signature.Length; index++)
        {
            if (signature[index] == oldElementType)
                matches.Add(index);
        }
        Assert.HasCount(1, matches);
        signature[matches[0]] = newElementType;
    }

    private static void ReplaceGenericParameterNullableTransformWithArray(
        byte[] bytes,
        string typeName,
        string methodName)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var method = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(item => metadata.GetString(item.Name) == methodName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        var parameter = method.GetGenericParameters()
            .Select(metadata.GetGenericParameter)
            .Single();
        var target = parameter.GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Single(item => GetAttributeTypeName(metadata, item) == "NullableAttribute");
        var source = metadata.CustomAttributes
            .Select(metadata.GetCustomAttribute)
            .First(item => GetAttributeTypeName(metadata, item) == "NullableAttribute"
                && IsArrayAttributeConstructor(metadata, item.Constructor)
                && metadata.GetBlobBytes(item.Value).Length >= 10);

        var row = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(row, EncodeHasCustomAttribute(target.Parent));
        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(2), EncodeCustomAttributeType(target.Constructor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            row.AsSpan(4),
            checked((ushort)MetadataTokens.GetHeapOffset(target.Value)));
        var matches = Enumerable.Range(0, bytes.Length - row.Length + 1)
            .Where(index => bytes.AsSpan(index, row.Length).SequenceEqual(row))
            .ToArray();
        Assert.HasCount(1, matches);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(matches[0] + 2, sizeof(ushort)),
            EncodeCustomAttributeType(source.Constructor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(matches[0] + 4, sizeof(ushort)),
            checked((ushort)MetadataTokens.GetHeapOffset(source.Value)));
    }

    private static bool IsArrayAttributeConstructor(
        MetadataReader metadata,
        EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
            return false;
        var signature = metadata.GetBlobBytes(
            metadata.GetMemberReference((MemberReferenceHandle)constructor).Signature);
        return signature.Contains((byte)0x1D);
    }

    private static void ChangeMethodAttributes(
        byte[] bytes,
        string typeName,
        string methodName,
        Func<MethodAttributes, MethodAttributes> change)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var method = metadata.MethodDefinitions
            .Select(metadata.GetMethodDefinition)
            .Single(item => metadata.GetString(item.Name) == methodName
                && metadata.GetString(metadata.GetTypeDefinition(item.GetDeclaringType()).Name) == typeName);
        Assert.IsTrue(metadata.GetHeapSize(HeapIndex.String) < ushort.MaxValue);
        Assert.IsTrue(metadata.GetHeapSize(HeapIndex.Blob) < ushort.MaxValue);

        var rowPrefix = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(rowPrefix, method.RelativeVirtualAddress);
        BinaryPrimitives.WriteUInt16LittleEndian(rowPrefix.AsSpan(4), (ushort)method.ImplAttributes);
        BinaryPrimitives.WriteUInt16LittleEndian(rowPrefix.AsSpan(6), (ushort)method.Attributes);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rowPrefix.AsSpan(8),
            checked((ushort)MetadataTokens.GetHeapOffset(method.Name)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            rowPrefix.AsSpan(10),
            checked((ushort)MetadataTokens.GetHeapOffset(method.Signature)));
        var matches = Enumerable.Range(0, bytes.Length - rowPrefix.Length + 1)
            .Where(index => bytes.AsSpan(index, rowPrefix.Length).SequenceEqual(rowPrefix))
            .ToArray();
        Assert.HasCount(1, matches, "Expected one MethodDef row prefix.");
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(matches[0] + 6, sizeof(ushort)),
            (ushort)change(method.Attributes));
    }

    private static void ChangeTypeAttributes(
        byte[] bytes,
        string typeName,
        Func<TypeAttributes, TypeAttributes> change)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        var metadata = peReader.GetMetadataReader();
        var type = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(item => metadata.GetString(item.Name) == typeName);
        Assert.IsTrue(metadata.GetHeapSize(HeapIndex.String) < ushort.MaxValue);

        var rowPrefix = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(rowPrefix, (uint)type.Attributes);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rowPrefix.AsSpan(4),
            checked((ushort)MetadataTokens.GetHeapOffset(type.Name)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            rowPrefix.AsSpan(6),
            checked((ushort)MetadataTokens.GetHeapOffset(type.Namespace)));
        var matches = Enumerable.Range(0, bytes.Length - rowPrefix.Length + 1)
            .Where(index => bytes.AsSpan(index, rowPrefix.Length).SequenceEqual(rowPrefix))
            .ToArray();
        Assert.HasCount(1, matches, "Expected one TypeDef row prefix.");
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(matches[0], sizeof(uint)),
            (uint)change(type.Attributes));
    }

    private static int GetMetadataStreamFileOffset(
        byte[] bytes,
        int metadataStart,
        string streamName)
    {
        var versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(metadataStart + 12, sizeof(int)));
        var position = Align4(metadataStart + 16 + versionLength);
        var streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(position + sizeof(ushort), sizeof(ushort)));
        position += sizeof(ushort) * 2;
        for (var index = 0; index < streamCount; index++)
        {
            var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(position, sizeof(int)));
            position += sizeof(int) * 2;
            var nameEnd = Array.IndexOf(bytes, (byte)0, position);
            var name = Encoding.ASCII.GetString(bytes, position, nameEnd - position);
            position = Align4(nameEnd + 1);
            if (name == streamName)
                return metadataStart + relativeOffset;
        }
        Assert.Fail($"Metadata stream '{streamName}' was not found.");
        return 0;
    }

    private static int GetCompressedIntegerSize(byte firstByte) => firstByte switch
    {
        < 0x80 => 1,
        < 0xC0 => 2,
        _ => 4,
    };

    private static int Align4(int value) => (value + 3) & ~3;

    private sealed record SignatureLocation(
        int PrefixOffset,
        int DataOffset,
        int Length,
        int PrefixSize);
}
