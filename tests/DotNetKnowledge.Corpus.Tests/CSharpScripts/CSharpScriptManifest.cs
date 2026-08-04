using DotNetKnowledge.CSharpScriptHost;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

internal static class CSharpScriptManifest
{
    private const string ScriptHeading = "## C# scripts (`.csx`)";
    private const string TableHeader = "| Scenario | Entry | Hosts | Demonstrates | Note |";
    private const string TableSeparator = "|---|---|---|---|---|";

    public static IReadOnlyList<ScriptManifestRow> Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var lines = File.ReadAllLines(manifestPath);
        var headingIndex = Array.FindIndex(lines, line => line == ScriptHeading);
        if (headingIndex < 0)
        {
            throw Error(manifestPath, 1, $"Required heading is missing: {ScriptHeading}.");
        }

        var sectionEndIndex = Array.FindIndex(
            lines,
            headingIndex + 1,
            line => line.StartsWith("## ", StringComparison.Ordinal));
        if (sectionEndIndex < 0)
        {
            sectionEndIndex = lines.Length;
        }

        var headerIndex = FirstNonblankLine(lines, headingIndex + 1, sectionEndIndex);
        if (headerIndex < 0 || lines[headerIndex] != TableHeader)
        {
            var errorIndex = headerIndex < 0 ? sectionEndIndex : headerIndex;
            throw Error(manifestPath, errorIndex + 1, $"Expected exact script table header: {TableHeader}.");
        }

        var separatorIndex = headerIndex + 1;
        if (separatorIndex >= sectionEndIndex || lines[separatorIndex] != TableSeparator)
        {
            throw Error(manifestPath, separatorIndex + 1, $"Expected exact script table separator: {TableSeparator}.");
        }

        var rows = new List<ScriptManifestRow>();
        var idLines = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = separatorIndex + 1; index < sectionEndIndex; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith('|') || !line.EndsWith('|'))
            {
                throw Error(manifestPath, index + 1, "Unexpected content after the script table.");
            }

            var columns = line.Split('|');
            if (columns.Length != 7)
            {
                throw Error(manifestPath, index + 1, "A script manifest row must contain exactly five columns.");
            }

            var values = columns[1..^1].Select(TrimCell).ToArray();
            RequireValue(manifestPath, index + 1, values[0], "Scenario");
            RequireValue(manifestPath, index + 1, values[1], "Entry");
            RequireValue(manifestPath, index + 1, values[2], "Hosts");
            RequireValue(manifestPath, index + 1, values[3], "Demonstrates");

            if (idLines.TryGetValue(values[0], out var firstLine))
            {
                throw Error(
                    manifestPath,
                    index + 1,
                    $"Duplicate script scenario ID '{values[0]}' (first declared at line {firstLine}).");
            }

            var hosts = ParseHosts(manifestPath, index + 1, values[2]);
            idLines.Add(values[0], index + 1);
            rows.Add(new ScriptManifestRow(values[0], values[1], hosts, values[3], values[4]));
        }

        if (rows.Count == 0)
        {
            throw Error(manifestPath, sectionEndIndex + 1, "The script table must contain at least one row.");
        }

        return rows;
    }

    private static List<ScriptHostKind> ParseHosts(string manifestPath, int lineNumber, string value)
    {
        var hosts = new List<ScriptHostKind>();
        foreach (var hostValue in value.Split(','))
        {
            var hostName = hostValue.Trim().Trim('`').Trim();
            if (!Enum.TryParse<ScriptHostKind>(hostName, ignoreCase: true, out var host) ||
                !Enum.IsDefined(host))
            {
                throw Error(manifestPath, lineNumber, $"Unknown script host '{hostName}'.");
            }

            if (hosts.Contains(host))
            {
                throw Error(manifestPath, lineNumber, $"Duplicate script host '{hostName}'.");
            }

            hosts.Add(host);
        }

        return hosts;
    }

    private static int FirstNonblankLine(string[] lines, int startIndex, int endIndex)
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string TrimCell(string value) => value.Trim().Trim('`').Trim();

    private static void RequireValue(string manifestPath, int lineNumber, string value, string column)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Error(manifestPath, lineNumber, $"Script manifest column '{column}' must not be blank.");
        }
    }

    private static InvalidDataException Error(string manifestPath, int lineNumber, string message) =>
        new($"{manifestPath}: line {lineNumber}: {message}");
}

internal sealed record ScriptManifestRow(
    string Id,
    string Entry,
    IReadOnlyList<ScriptHostKind> Hosts,
    string Demonstrates,
    string Note);
