using System.Text;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class PackageXmlDocsReaderTests
{
    [TestMethod]
    public void ReadRendersCompilerDocumentationElements()
    {
        const string xml = """
            <doc><members><member name="M:Fixtures.Type.Run(System.String)">
            <summary>Uses <see cref="T:System.String"/> and <seealso cref="M:Fixtures.Type.Other"/>.</summary>
            <param name="value">The <paramref name="value"/>.</param>
            <typeparam name="T">The <typeparamref name="T"/> value.</typeparam>
            <returns>A <see langword="null"/> result.</returns><value>The value.</value>
            <remarks>See <see href="https://example.test"/>.</remarks>
            <exception cref="T:System.InvalidOperationException">When invalid.</exception>
            </member></members></doc>
            """;

        var docs = PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        var documentation = docs["M:Fixtures.Type.Run(System.String)"];

        Assert.AreEqual("Uses System.String and Fixtures.Type.Other.", documentation.Summary);
        CollectionAssert.AreEqual(
            new[] { new ApiNamedDocumentation("value", "The value.") }, documentation.Parameters.ToArray());
        CollectionAssert.AreEqual(
            new[] { new ApiNamedDocumentation("T", "The T value.") }, documentation.TypeParameters.ToArray());
        Assert.AreEqual("A null result.", documentation.Returns);
        Assert.AreEqual("The value.", documentation.Value);
        Assert.AreEqual("See https://example.test.", documentation.Remarks);
        CollectionAssert.AreEqual(
            new[] { new ApiNamedDocumentation("T:System.InvalidOperationException", "When invalid.") },
            documentation.Exceptions.ToArray());
    }

    [TestMethod]
    public void ReadRejectsDuplicateIdsAndMalformedRoots()
    {
        const string duplicate = """
            <doc><members><member name="T:Fixtures.Type"/><member name="T:Fixtures.Type"/></members></doc>
            """;
        const string malformedRoot = "<documentation><members/></documentation>";

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(duplicate))));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(malformedRoot))));
    }

    [TestMethod]
    public void ReadRejectsDtdsAndKeepsOrdinallyDistinctIds()
    {
        const string dtd = """
            <!DOCTYPE doc [<!ENTITY injected "not allowed">]>
            <doc><members><member name="T:Fixtures.Type"><summary>&injected;</summary></member></members></doc>
            """;
        const string caseDistinct = """
            <doc><members>
            <member name="T:Fixtures.Type"><summary>Upper.</summary></member>
            <member name="T:fixtures.Type"><summary>Lower.</summary></member>
            </members></doc>
            """;

        Assert.ThrowsExactly<InvalidDataException>(() =>
            PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(dtd))));

        var docs = PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(caseDistinct)));

        Assert.AreEqual("Upper.", docs["T:Fixtures.Type"].Summary);
        Assert.AreEqual("Lower.", docs["T:fixtures.Type"].Summary);
    }
}
