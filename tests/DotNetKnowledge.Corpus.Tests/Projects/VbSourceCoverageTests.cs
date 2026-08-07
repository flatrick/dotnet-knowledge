using DotNetKnowledge.CorpusTooling;

namespace DotNetKnowledge.Corpus.Tests.Projects;

// Coverage here is computed at file granularity, not directory granularity, and is Remove-aware:
// for each project, a Compile Remove glob subtracts the files it matches from that project's
// Compile Include set before the result counts toward corpus coverage. This test requires each
// corpus file to survive in at least one project's Include-minus-Remove set.
//
// The resolution itself lives in scripts/shared/CompileItems.cs, linked into this project and
// #:include'd by scripts/verify-feature-floors.cs, so the two guards that read a VB project's
// Compile items cannot drift apart. That file also carries the reasoning for the file-granular,
// Remove-aware reading and for the one glob shape it models.
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

    // The set of .vb files this family's projects actually compile. A file only counts as covered
    // if some project's net Include-minus-Remove result still contains it.
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

            compiled.UnionWith(CompileItems.Resolve(project, ".vb").Included);
        }

        return compiled;
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
