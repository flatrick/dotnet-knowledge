using System.Text;
using System.Xml.Linq;
using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

internal static class DocumentationTextRenderer
{
    internal static string? Render(XElement? element)
    {
        if (element is null)
            return null;

        var builder = new StringBuilder();
        AppendNodes(element, builder);
        return DocumentationText.Normalize(
            builder.ToString(), collapseWhitespace: !element.Descendants("format").Any());
    }

    internal static string? StripIdPrefix(string? cref) =>
        cref is { Length: > 2 } && cref[1] == ':' && char.IsAsciiLetter(cref[0]) ? cref[2..] : cref;

    private static void AppendNodes(XElement parent, StringBuilder builder)
    {
        foreach (var node in parent.Nodes())
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement child:
                    AppendElement(child, builder);
                    break;
            }
        }
    }

    private static void AppendElement(XElement element, StringBuilder builder)
    {
        switch (element.Name.LocalName)
        {
            case "see":
            case "seealso":
                var reference = StripIdPrefix(element.Attribute("cref")?.Value)
                    ?? element.Attribute("langword")?.Value
                    ?? element.Attribute("href")?.Value;
                if (reference is not null && element.IsEmpty)
                {
                    builder.Append(reference);
                    return;
                }
                if (element.IsEmpty)
                    return;
                break;
            case "paramref":
            case "typeparamref":
                var name = element.Attribute("name")?.Value;
                if (name is not null)
                {
                    builder.Append(name);
                    return;
                }
                break;
        }

        AppendNodes(element, builder);
    }
}
