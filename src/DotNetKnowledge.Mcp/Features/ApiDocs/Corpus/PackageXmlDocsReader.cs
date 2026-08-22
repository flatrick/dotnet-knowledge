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
        ReadNamed(member, "param", "name", mergeDuplicates: false),
        ReadNamed(member, "typeparam", "name", mergeDuplicates: false),
        DocumentationTextRenderer.Render(member.Element("returns")),
        DocumentationTextRenderer.Render(member.Element("value")),
        DocumentationTextRenderer.Render(member.Element("remarks")),
        // Unlike a repeated <param>/<typeparam> name, a repeated <exception cref> is not a broken
        // signature -- real, official package XML (Microsoft.Build 18.9.6) documents the same
        // exception type twice on one member for two distinct reasons, and both must survive.
        ReadNamed(member, "exception", "cref", mergeDuplicates: true));

    private static ApiNamedDocumentation[] ReadNamed(
        XElement member, string elementName, string attributeName, bool mergeDuplicates)
    {
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var items = new List<ApiNamedDocumentation>();
        foreach (var element in member.Elements(elementName))
        {
            var name = element.Attribute(attributeName)?.Value;
            if (name is null)
            {
                // A missing name/cref cannot mean a broken signature the way it would for
                // <param>/<typeparam>, and the corpus schema requires every exception entry to name
                // a canonical type -- there is nothing valid to key it by. Real, official package
                // XML (Microsoft.Build 18.9.6) omits cref and names the exception type in prose
                // instead; that entry is dropped, like an undecodable attribute argument costs the
                // attribute, not the declaration.
                if (!mergeDuplicates)
                    throw new InvalidDataException($"A package XML {elementName} has no {attributeName} attribute.");
                continue;
            }

            var text = DocumentationTextRenderer.Render(element) ?? string.Empty;
            if (indexByName.TryGetValue(name, out var existingIndex))
            {
                if (!mergeDuplicates)
                    throw new InvalidDataException($"Duplicate package XML {elementName} {attributeName} '{name}'.");

                var existing = items[existingIndex];
                items[existingIndex] = existing with
                {
                    Text = existing.Text.Length == 0 || text.Length == 0
                        ? existing.Text + text
                        : $"{existing.Text} {text}",
                };
                continue;
            }

            indexByName[name] = items.Count;
            items.Add(new ApiNamedDocumentation(name, text));
        }
        return items
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Text, StringComparer.Ordinal)
            .ToArray();
    }
}
