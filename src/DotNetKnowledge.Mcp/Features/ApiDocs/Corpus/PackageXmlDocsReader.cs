using System.Xml;
using System.Xml.Linq;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public static class PackageXmlDocsReader
{
    public static IReadOnlyDictionary<string, ApiDocumentation> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The package XML documentation is malformed.", exception);
        }

        var root = document.Root;
        if (root?.Name.LocalName != "doc" || root.Elements("members").Count() != 1)
            throw new InvalidDataException("The package XML documentation must have one doc/members root.");

        var docs = new Dictionary<string, ApiDocumentation>(StringComparer.Ordinal);
        foreach (var member in root.Element("members")!.Elements("member"))
        {
            var id = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("A package XML documentation member has no ECMA ID.");
            if (!docs.TryAdd(id, ReadDocumentation(member)))
                throw new InvalidDataException($"Duplicate XML documentation ECMA ID '{id}'.");
        }

        return docs;
    }

    private static ApiDocumentation ReadDocumentation(XElement member) => new(
        DocumentationTextRenderer.Render(member.Element("summary")),
        ReadNamed(member, "param", "name"),
        ReadNamed(member, "typeparam", "name"),
        DocumentationTextRenderer.Render(member.Element("returns")),
        DocumentationTextRenderer.Render(member.Element("value")),
        DocumentationTextRenderer.Render(member.Element("remarks")),
        ReadNamed(member, "exception", "cref"));

    private static ApiNamedDocumentation[] ReadNamed(
        XElement member, string elementName, string attributeName) => member.Elements(elementName)
        .Select(element => new ApiNamedDocumentation(
            element.Attribute(attributeName)?.Value
                ?? throw new InvalidDataException($"A package XML {elementName} has no {attributeName} attribute."),
            DocumentationTextRenderer.Render(element) ?? string.Empty))
        .OrderBy(item => item.Name, StringComparer.Ordinal)
        .ThenBy(item => item.Text, StringComparer.Ordinal)
        .ToArray();
}
