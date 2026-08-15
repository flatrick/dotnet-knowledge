namespace DotNetKnowledge.Yaml;

/// <summary>
/// Microsoft Learn stamps a schema marker on a YAML document's first line. It is the only reliable
/// way to tell a documentation file from a build pipeline definition that happens to share the
/// extension: of the .yml files in the synchronized sources, nine are Azure Pipelines definitions
/// and two are prose.
/// </summary>
public static class LearnYamlMime
{
    /// <summary>The one schema this server renders and serves.</summary>
    public const string Faq = "FAQ";

    private const string Prefix = "### YamlMime:";

    /// <summary>
    /// The schema name on the document's first non-blank line, or null when there is no marker.
    /// Only that line is examined: a marker further down would let any file claim a schema.
    /// </summary>
    public static string? Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var remaining = text.AsSpan().TrimStart('﻿');
        while (!remaining.IsEmpty)
        {
            var breakIndex = remaining.IndexOf('\n');
            var line = (breakIndex < 0 ? remaining : remaining[..breakIndex]).Trim();
            if (!line.IsEmpty)
            {
                return line.StartsWith(Prefix, StringComparison.Ordinal)
                    ? line[Prefix.Length..].Trim().ToString()
                    : null;
            }

            if (breakIndex < 0)
                break;

            remaining = remaining[(breakIndex + 1)..];
        }

        return null;
    }
}
