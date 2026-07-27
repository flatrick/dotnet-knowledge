using System.Xml.Linq;

namespace DotNetKnowledge.Corpus.Tests.Projects;

// Coverage here is computed at file granularity, not directory granularity, and is Remove-aware:
// for each project, a Compile Remove glob subtracts the files it matches from that project's
// Compile Include set before the result counts toward corpus coverage. A directory-prefix check
// would call a file "covered" merely because it sits under a directory some project's Include
// glob names, even if a Remove in that same project excludes it and no other project references
// it. This test resolves every glob to the .vb files that actually exist on disk and requires
// each corpus file to survive in at least one project's Include-minus-Remove set. It does not
// model any MSBuild glob shape beyond this corpus's own "<directory>/**/*.vb" convention — see
// ResolveGlob.
[TestClass]
[TestCategory("Unit")]
public sealed class VbSourceCoverageTests
{
    [TestMethod]
    public void EveryVbSourceFileIsCompiledByAtLeastOneProject()
    {
        var repositoryRoot = RepositoryRoot();
        var uncovered = new List<string>();

        foreach (var familyRoot in VbFamilyRoots(repositoryRoot))
        {
            var sourceRoot = Path.Combine(familyRoot, "src");
            var covered = CompiledFiles(familyRoot);

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.vb", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                if (!covered.Contains(full))
                {
                    uncovered.Add(Path.GetRelativePath(repositoryRoot, full).Replace('\\', '/'));
                }
            }
        }

        Assert.IsTrue(
            uncovered.Count == 0,
            $"These VB source files are in the corpus but no project compiles them:{Environment.NewLine}" +
            string.Join(Environment.NewLine, uncovered));
    }

    // The set of .vb files this family's projects actually compile: each project's Compile
    // Include globs, resolved to files on disk, minus its Compile Remove globs, resolved the
    // same way. A file only counts as covered if some project's net result still contains it.
    private static HashSet<string> CompiledFiles(string familyRoot)
    {
        var compiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in Directory.EnumerateFiles(familyRoot, "*.vbproj", SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(project)!;
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var compile in XDocument.Load(project).Descendants().Where(element => element.Name.LocalName == "Compile"))
            {
                var includeGlob = compile.Attribute("Include")?.Value;
                if (!string.IsNullOrWhiteSpace(includeGlob))
                {
                    included.UnionWith(ResolveGlob(project, projectDirectory, includeGlob));
                }

                var removeGlob = compile.Attribute("Remove")?.Value;
                if (!string.IsNullOrWhiteSpace(removeGlob))
                {
                    removed.UnionWith(ResolveGlob(project, projectDirectory, removeGlob));
                }
            }

            included.ExceptWith(removed);
            compiled.UnionWith(included);
        }

        return compiled;
    }

    // Every Compile Include/Remove glob in this corpus has the shape "<directory>/**/*.vb". Resolve
    // that to the .vb files actually present under the directory rather than implementing a general
    // glob matcher. A glob that does not fit this shape throws, so a future pattern change fails the
    // test loudly instead of silently under- or over-counting coverage.
    private static IEnumerable<string> ResolveGlob(string project, string projectDirectory, string glob)
    {
        const string tailPattern = "**/*.vb";
        if (!glob.EndsWith(tailPattern, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unrecognized Compile glob \"{glob}\" in {project}: expected it to end in \"{tailPattern}\".");
        }

        var directoryPart = glob[..^tailPattern.Length];
        var directory = Path.GetFullPath(Path.Combine(projectDirectory, directoryPart)).TrimEnd(Path.DirectorySeparatorChar);

        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.vb", SearchOption.AllDirectories)
            : [];
    }

    private static IEnumerable<string> VbFamilyRoots(string repositoryRoot)
    {
        var vbRoot = Path.Combine(repositoryRoot, "examples", "language-features", "VB.NET");
        yield return Path.Combine(vbRoot, "dotnet", "Net10");
        yield return Path.Combine(vbRoot, "dotNetFramework", "v4.8");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
