using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiDeclarationIdTests
{
    [TestMethod]
    public void TypeAndNamespaceIdsAcceptUnicodeMetadataIdentifiers()
    {
        var names = new[]
        {
            "A\u0301ccent.Type",
            "Connector\u203FName.Type",
            "Format\u200CName.Type",
            "Supplementary\U00010400Name.Type",
        };

        foreach (var name in names)
        {
            Assert.IsTrue(ApiDeclarationId.IsCanonicalNamespaceName(name));
            Assert.IsTrue(ApiDeclarationId.IsCanonicalTypeName(name));
            Assert.IsTrue(ApiDeclarationId.IsCanonicalTypeId("T:" + name));
        }
    }

    [TestMethod]
    public void TypeAndNamespaceIdsRejectMalformedArityAndSegments()
    {
        var invalidNamespaces = new[]
        {
            "Fixture`1.Namespace",
            "Fixture..Namespace",
            ".Fixture",
            "Fixture.",
        };
        var invalidTypeIds = new[]
        {
            "T:Fixture.Type`",
            "T:Fixture.Type`0",
            "T:Fixture.Type`01",
            "T:Fixture.Type`1Extra",
            "T:Fixture.`1Type",
        };

        foreach (var name in invalidNamespaces)
            Assert.IsFalse(ApiDeclarationId.IsCanonicalNamespaceName(name), name);
        foreach (var id in invalidTypeIds)
            Assert.IsFalse(ApiDeclarationId.IsCanonicalTypeId(id), id);
    }

    [TestMethod]
    public void MemberIdsAcceptEcmaSignaturesProducedByMetadata()
    {
        const string typeId = "T:Fixture.Type`1";
        var ids = new[]
        {
            "M:Fixture.Type`1.#ctor(System.Int32)",
            "M:Fixture.Type`1.Method``1(`0@,``0*,System.String[],System.Int32[0:,0:])",
            "M:Fixture.Type`1.WithBounds(System.Int32[-1:4])",
            "M:Fixture.Type`1.op_Explicit(Fixture.Type{`0})~System.Int32",
            "M:Fixture.Type`1.Interface#Method(System.String)",
            "M:Fixture.Type`1.Use(`0@|System.Runtime.InteropServices.InAttribute)",
            "M:Fixture.Type`1.Use(=FUNC:System.Void(System.IntPtr))",
            "P:Fixture.Type`1.Item(System.Int32)",
            "F:Fixture.Type`1.Field",
            "E:Fixture.Type`1.Event",
        };

        foreach (var id in ids)
            Assert.IsTrue(ApiDeclarationId.IsCanonicalMemberId(id, typeId), id);
    }

    [TestMethod]
    public void MemberIdsRejectMalformedOwnedSuffixes()
    {
        const string typeId = "T:Fixture.Type`1";
        var ids = new[]
        {
            "M:Fixture.Type`1.Run/Bad",
            "M:Fixture.Type`1.Run(",
            "M:Fixture.Type`1.Run)",
            "M:Fixture.Type`1.Run()",
            "M:Fixture.Type`1.Run(System.String,)",
            "M:Fixture.Type`1.Run(,System.String)",
            "M:Fixture.Type`1.Run(System.String[]]",
            "M:Fixture.Type`1.Run(System.List{System.String)",
            "M:Fixture.Type`1.Run(System.Int32[-:])",
            "M:Fixture.Type`1.Run(System.Int32[00:])",
            "M:Fixture.Type`1.Run(System.Int32[-0:])",
            "M:Fixture.Type`1.Run(System.Int32[0:0])",
            "M:Fixture.Type`1.Run(System.Int32[2147483648:])",
            "M:Fixture.Type`1.Run(System.Int32[0:2147483648])",
            "M:Fixture.Type`1.Run``",
            "M:Fixture.Type`1.Run``0",
            "M:Fixture.Type`1.Run(System.String)~",
            "M:Fixture.Type`1.Run~System.String~System.Int32",
            "M:Fixture.Type`1.op_Implicit~System.Int32",
            "M:Fixture.Type`1.op_Implicit(System.Int32,System.String)~System.Int32",
            "P:Fixture.Type`1.Item~System.String",
            "F:Fixture.Type`1.Field(System.String)",
            "E:Fixture.Type`1.Event(System.String)",
        };

        foreach (var id in ids)
            Assert.IsFalse(ApiDeclarationId.IsCanonicalMemberId(id, typeId), id);
    }
}
