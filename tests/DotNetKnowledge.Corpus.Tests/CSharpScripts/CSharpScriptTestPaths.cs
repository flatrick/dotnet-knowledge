namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

internal static class CSharpScriptTestPaths
{
    public static string RepositoryRoot => FindRepositoryRoot();

    public static string ShowcaseRoot
    {
        get
        {
            var showcaseRoot = Path.Combine(
                RepositoryRoot,
                "examples",
                "language-features",
                "CSharp",
                "csx",
                "roslyn-5.6.0");
            return Directory.Exists(showcaseRoot)
                ? showcaseRoot
                : throw new InvalidOperationException($"C# script showcase root does not exist: {showcaseRoot}.");
        }
    }

    public static string Descriptor(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var descriptor = Path.GetFullPath(Path.Combine(ShowcaseRoot, "examples", id, "scenario.json"));
        var examplesRoot = Path.Combine(ShowcaseRoot, "examples");
        var relative = Path.GetRelativePath(examplesRoot, descriptor);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scenario descriptor escapes the examples directory: {id}.");
        }

        return File.Exists(descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Scenario descriptor does not exist: {descriptor}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test base directory.");
    }
}
