#nullable enable

using System.Xml.Linq;

namespace DotNetKnowledge.CorpusTooling;

// Resolving a VB project's Compile items to a set of files on disk, shared by the two consumers that
// need the answer: tests/DotNetKnowledge.Corpus.Tests/Projects/VbSourceCoverageTests.cs, which asks
// whether a corpus row is compiled by anything, and scripts/verify-feature-floors.cs, which asks
// which rows a project owns. The two used to implement this separately and agreed only by
// inspection; drift between them would produce a blind spot precisely where two guards appear to
// agree, since each would report a clean run while disagreeing about which files exist.
//
// The script names this file with `#:include shared/CompileItems.cs` and the test project with a
// linked <Compile Include>. A file-based program does not glob its own directory, so no other script
// picks this up.
//
// Compiled by two hosts with different settings: the test project sets Nullable=enable and inherits
// TreatWarningsAsErrors and AnalysisLevel=latest-recommended from the root Directory.Build.props,
// while scripts/Directory.Build.props resets all three. The file-level #nullable enable above is
// what keeps the nullable semantics from depending on which host compiled it; the rest of the file
// must stay clean under the stricter one.
internal static class CompileItems
{
    // The files a single project compiles: its Compile Include globs resolved to files on disk,
    // minus its Compile Remove globs resolved the same way. Coverage is therefore computed at file
    // granularity and is Remove-aware. A directory-prefix check would call a file "compiled" merely
    // because it sits under a directory some Include glob names, even where a Remove in the same
    // project excludes it — which is exactly how MyNamespaceHelpers would end up attributed to every
    // net48 library project, none of which compiles it.
    //
    // Both halves are returned because the consumers need different ones. The coverage test asks
    // only whether a file survives in some project. The floor probe's under-placement check needs
    // the removals too: a row a project deliberately excludes is a policy statement, not a row it
    // forgot to claim.
    internal static (HashSet<string> Included, HashSet<string> Removed) Resolve(
        string projectPath, string sourceExtension)
    {
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        foreach (var compile in XDocument.Load(projectPath).Descendants()
                     .Where(element => element.Name.LocalName == "Compile"))
        {
            var includeGlob = compile.Attribute("Include")?.Value;
            if (!string.IsNullOrWhiteSpace(includeGlob))
            {
                included.UnionWith(ResolveGlob(projectPath, projectDirectory, includeGlob, sourceExtension));
            }

            var removeGlob = compile.Attribute("Remove")?.Value;
            if (!string.IsNullOrWhiteSpace(removeGlob))
            {
                removed.UnionWith(ResolveGlob(projectPath, projectDirectory, removeGlob, sourceExtension));
            }
        }

        included.ExceptWith(removed);
        return (included, removed);
    }

    // Every Compile Include/Remove glob in this corpus has the shape "<directory>/**/*<extension>".
    // Resolve that to the source files actually present under the directory rather than implementing
    // a general glob matcher. A glob that does not fit this shape throws, so a future pattern change
    // fails loudly instead of silently under- or over-counting.
    internal static IEnumerable<string> ResolveGlob(
        string projectPath, string projectDirectory, string glob, string sourceExtension)
    {
        var tailPattern = "**/*" + sourceExtension;
        if (!glob.EndsWith(tailPattern, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unrecognized Compile glob \"{glob}\" in {projectPath}: expected it to end in \"{tailPattern}\".");
        }

        var directoryPart = glob[..^tailPattern.Length];
        var directory = Path.GetFullPath(Path.Combine(projectDirectory, directoryPart))
            .TrimEnd(Path.DirectorySeparatorChar);

        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*" + sourceExtension, SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
            : [];
    }
}
