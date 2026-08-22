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

    [TestMethod]
    public void ReadRejectsDuplicateParameterAndTypeParameterKeys()
    {
        var duplicates = new[]
        {
            "<param name=\"value\"></param><param name=\"value\">Second.</param>",
            "<typeparam name=\"T\"></typeparam><typeparam name=\"T\">Second.</typeparam>",
        };

        foreach (var duplicate in duplicates)
        {
            var xml = $"<doc><members><member name=\"M:Fixtures.Type.Run\">{duplicate}</member></members></doc>";
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
        }
    }

    // Real, official package XML (e.g. Microsoft.Build 18.9.6) documents the same exception type
    // twice on one member for two distinct reasons -- redundant authoring, not corruption, unlike a
    // repeated <param>/<typeparam> name, which really would indicate a broken signature. Both
    // reasons must survive, or the second one is silently dropped.
    [TestMethod]
    public void ReadMergesDuplicateExceptionCrefsInsteadOfRejectingThem()
    {
        const string xml = """
            <doc><members><member name="M:Fixtures.Type.Run">
            <exception cref="T:System.InvalidOperationException">When null.</exception>
            <exception cref="T:System.InvalidOperationException">When already shut down.</exception>
            </member></members></doc>
            """;

        var documentation = PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            .Values.Single();

        var exception = documentation.Exceptions.Single();
        Assert.AreEqual("T:System.InvalidOperationException", exception.Name);
        StringAssert.Contains(exception.Text, "When null.");
        StringAssert.Contains(exception.Text, "When already shut down.");
    }

    // Real, official package XML (Microsoft.Build 18.9.6,
    // P:Microsoft.Build.Execution.BuildResult.Item(System.String)) also omits cref entirely and
    // names the exception type in prose instead. The corpus schema requires every exception entry
    // to name a canonical type, so there is nothing valid to key an uncref'd entry by; it is
    // dropped like an undecodable attribute argument costs the attribute, not the declaration --
    // the member and its other, well-formed exceptions still survive.
    [TestMethod]
    public void ReadDropsExceptionsWithNoCrefInsteadOfRejectingOrKeepingThem()
    {
        const string xml = """
            <doc><members><member name="P:Fixtures.Type.Item(System.String)">
            <exception>KeyNotFoundException when the target doesn't exist.</exception>
            <exception cref="T:System.ArgumentException">When the target already has results.</exception>
            </member></members></doc>
            """;

        var documentation = PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            .Values.Single();

        var exception = documentation.Exceptions.Single();
        Assert.AreEqual("T:System.ArgumentException", exception.Name);
        Assert.AreEqual("When the target already has results.", exception.Text);
    }

    [TestMethod]
    public void ReadRejectsAParamOrTypeParamWithNoNameAttribute()
    {
        var missingName = new[]
        {
            "<param>Second.</param>",
            "<typeparam>Second.</typeparam>",
        };

        foreach (var xmlFragment in missingName)
        {
            var xml = $"<doc><members><member name=\"M:Fixtures.Type.Run\">{xmlFragment}</member></members></doc>";
            Assert.ThrowsExactly<InvalidDataException>(() =>
                PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
        }
    }

    [TestMethod]
    public void ReadKeepsNamedDocumentationKeySpacesSeparateAndAcceptsEmptyText()
    {
        const string xml = """
            <doc><members><member name="M:Fixtures.Type.Run">
            <param name="T"></param><typeparam name="T"></typeparam>
            <exception cref="T:System.Exception"></exception>
            </member></members></doc>
            """;

        var documentation = PackageXmlDocsReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            .Values.Single();

        Assert.AreEqual(string.Empty, documentation.Parameters.Single().Text);
        Assert.AreEqual(string.Empty, documentation.TypeParameters.Single().Text);
        Assert.AreEqual(string.Empty, documentation.Exceptions.Single().Text);
    }
}
