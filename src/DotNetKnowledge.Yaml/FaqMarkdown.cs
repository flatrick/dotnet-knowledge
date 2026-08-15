using System.Text;
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Yaml;

/// <summary>
/// Renders a FAQ to markdown. This is the seam: everything downstream - outline extraction, line
/// search, atomic blocks, paging, budgeting - runs on what this returns, unchanged, because by
/// then the document is markdown like any other.
/// </summary>
public static partial class FaqMarkdown
{
    public static string Render(FaqDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        // Title is deliberately not rendered. As an H1 it would be the ancestor of every heading in
        // the file, prefixing all of the document's section paths with its own name.
        if (!string.IsNullOrWhiteSpace(document.Summary))
            builder.Append(document.Summary.TrimEnd()).Append("\n\n");

        foreach (var section in document.Sections)
        {
            builder.Append("# ").Append(Flatten(section.Name)).Append("\n\n");

            foreach (var question in section.Questions)
            {
                builder.Append("## ").Append(Flatten(question.Question)).Append("\n\n");
                if (!string.IsNullOrWhiteSpace(question.Answer))
                    builder.Append(question.Answer.TrimEnd()).Append("\n\n");
            }
        }

        return builder.ToString();
    }

    // A heading has to be one line. A block scalar need not be.
    private static string Flatten(string value) => WhitespaceRun().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
