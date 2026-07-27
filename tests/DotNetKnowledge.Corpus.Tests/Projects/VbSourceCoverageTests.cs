using System.Xml.Linq;

namespace DotNetKnowledge.Corpus.Tests.Projects;

[TestClass]
[TestCategory("Unit")]
public sealed class VbSourceCoverageTests
{
    [TestMethod]
    public void EveryVbRowFolderIsCompiledByAtLeastOneProject()
    {
        var repositoryRoot = RepositoryRoot();
        var uncovered = new List<string>();

        foreach (var familyRoot in VbFamilyRoots(repositoryRoot))
        {
            var sourceRoot = Path.Combine(familyRoot, "src");
            var covered = CompiledDirectories(familyRoot);

            foreach (var versionDir in Directory.EnumerateDirectories(sourceRoot))
            {
                foreach (var rowDir in Directory.EnumerateDirectories(versionDir))
                {
                    var full = Path.GetFullPath(rowDir);
                    if (!covered.Any(prefix =>
                            full.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                            full.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    {
                        uncovered.Add(Path.GetRelativePath(repositoryRoot, full).Replace('\\', '/'));
                    }
                }
            }
        }

        Assert.IsTrue(
            uncovered.Count == 0,
            $"These row folders are in the corpus but no project compiles them:{Environment.NewLine}" +
            string.Join(Environment.NewLine, uncovered));
    }

    // Every Compile Include ends in a "/**/*.vb" glob tail; the directory in front of it is what
    // the project actually covers. Compile Remove items are not read here, so a Remove cannot make
    // an orphaned row look covered.
    private static List<string> CompiledDirectories(string familyRoot)
    {
        var directories = new List<string>();

        foreach (var project in Directory.EnumerateFiles(familyRoot, "*.vbproj", SearchOption.AllDirectories))
        {
            if (project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                project.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(project)!;
            foreach (var include in XDocument.Load(project)
                         .Descendants()
                         .Where(element => element.Name.LocalName == "Compile")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var tail = include!.IndexOf("**", StringComparison.Ordinal);
                var directoryPart = tail >= 0 ? include[..tail] : Path.GetDirectoryName(include) ?? "";
                directories.Add(Path.GetFullPath(Path.Combine(projectDirectory, directoryPart)).TrimEnd(Path.DirectorySeparatorChar));
            }
        }

        return directories;
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
