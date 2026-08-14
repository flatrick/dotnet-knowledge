namespace DotNetKnowledge.Mcp.Sources;

internal static class PortableFrameworkName
{
    private static readonly HashSet<string> WindowsReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Framework names become <framework>.json. Keep the check host-independent so a state
    // written on one operating system cannot become an unsafe path after it is moved to another.
    internal static bool IsSafe(string? framework) =>
        !string.IsNullOrWhiteSpace(framework)
        && !Path.IsPathRooted(framework)
        && !framework.Contains('/')
        && !framework.Contains('\\')
        && !framework.Contains(':')
        && framework is not "." and not ".."
        && !framework.EndsWith(' ')
        && !framework.EndsWith('.')
        && !framework.Any(character => char.IsControl(character) || character is '<' or '>' or '"' or '|' or '?' or '*')
        && !WindowsReservedFileNames.Contains(FileNameBase(framework));

    private static string FileNameBase(string framework)
    {
        var dot = framework.IndexOf('.');
        return dot < 0 ? framework : framework[..dot];
    }
}
