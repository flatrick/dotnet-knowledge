using System.Xml.Linq;
using DotNetKnowledge.CSharpScriptHost;
using DotNetKnowledge.Corpus.Tests.Projects;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Integration")]
public sealed class CSharpScriptCorpusCoverageTests
{
    private const string RoslynVersion = "5.6.0";
    private static readonly IReadOnlyDictionary<string, ScriptHostKind[]> ApprovedScenarios =
        new Dictionary<string, ScriptHostKind[]>(StringComparer.Ordinal)
        {
            ["command-line-arguments"] = [ScriptHostKind.Api, ScriptHostKind.Csi],
            ["continued-submissions"] = [ScriptHostKind.Api],
            ["expression-result"] = [ScriptHostKind.Api, ScriptHostKind.Csi],
            ["json-file-transform"] = [ScriptHostKind.Api],
            ["load-relative-script"] = [ScriptHostKind.Api, ScriptHostKind.Csi],
            ["reference-bcl-assembly"] = [ScriptHostKind.Api, ScriptHostKind.Csi],
            ["top-level-await"] = [ScriptHostKind.Api, ScriptHostKind.Csi],
            ["typed-globals"] = [ScriptHostKind.Api]
        };

    [TestMethod]
    public void ManifestDescriptorsAndScenarioFilesAreAnExactBijection()
    {
        var repositoryRoot = CSharpScriptTestPaths.RepositoryRoot;
        var languageFeaturesRoot = Path.Combine(repositoryRoot, "examples", "language-features");
        var examplesRoot = Path.Combine(CSharpScriptTestPaths.ShowcaseRoot, "examples");
        var manifest = CSharpScriptManifest.Load(Path.Combine(languageFeaturesRoot, "MANIFEST.md"));

        var scenarioDirectories = Directory.EnumerateDirectories(examplesRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var descriptorPaths = Directory.EnumerateFiles(examplesRoot, "scenario.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            scenarioDirectories.Select(directory => Path.Combine(directory, "scenario.json")).ToArray(),
            descriptorPaths,
            "Every immediate scenario directory must contain exactly one descriptor, with no nested scenarios.");

        var descriptors = descriptorPaths.Select(ScenarioDescriptorLoader.Load).ToArray();
        CollectionAssert.AreEqual(
            ApprovedScenarios.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            manifest.Select(row => row.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            "The manifest must contain exactly the eight approved script scenarios.");
        CollectionAssert.AreEqual(
            manifest.Select(row => row.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            descriptors.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            "Manifest scenario IDs must exactly equal descriptor IDs.");

        var canonicalPaths = new HashSet<string>(PathComparer());
        foreach (var descriptor in descriptors)
        {
            var scenarioDirectory = Path.GetDirectoryName(
                descriptorPaths.Single(path =>
                    string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), descriptor.Id, StringComparison.Ordinal)))!;
            Assert.AreEqual(
                descriptor.Id,
                Path.GetFileName(scenarioDirectory),
                $"Descriptor ID must match its scenario directory: {scenarioDirectory}.");

            var row = manifest.Single(candidate => candidate.Id == descriptor.Id);
            var declaredRelativePaths = new[] { "scenario.json", descriptor.Entry }
                .Concat(descriptor.SupportFiles)
                .ToArray();
            var declaredCanonicalPaths = declaredRelativePaths
                .Select(path => CanonicalContainedPath(scenarioDirectory, path))
                .ToArray();

            Assert.HasCount(
                declaredCanonicalPaths.Length,
                declaredCanonicalPaths.Distinct(PathComparer()).ToArray(),
                $"Scenario '{descriptor.Id}' declares duplicate canonical paths.");
            foreach (var path in declaredCanonicalPaths)
            {
                Assert.IsTrue(
                    canonicalPaths.Add(path),
                    $"Canonical path is declared by more than one scenario: {path}.");
            }

            var expectedFiles = declaredCanonicalPaths
                .Select(path => Path.GetRelativePath(scenarioDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var actualFiles = Directory.EnumerateFiles(scenarioDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(scenarioDirectory, Path.GetFullPath(path)).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                expectedFiles,
                actualFiles,
                $"Scenario '{descriptor.Id}' must contain only its descriptor, entry, and declared support files.");

            var canonicalEntry = CanonicalContainedPath(scenarioDirectory, descriptor.Entry);
            var repositoryRelativeEntry = Path.GetRelativePath(languageFeaturesRoot, canonicalEntry).Replace('\\', '/');
            Assert.AreEqual(
                repositoryRelativeEntry,
                row.Entry,
                $"Manifest entry must match descriptor '{descriptor.Id}'.");
            CollectionAssert.AreEqual(
                descriptor.Hosts.ToArray(),
                row.Hosts.ToArray(),
                $"Manifest hosts must match descriptor '{descriptor.Id}'.");
            CollectionAssert.AreEqual(
                ApprovedScenarios[descriptor.Id],
                descriptor.Hosts.ToArray(),
                $"Descriptor hosts must match the approved host matrix for '{descriptor.Id}'.");
        }
    }

    [TestMethod]
    public void RoslynPackagesAndShowcaseDirectoryUseTheExactVersionCoordinate()
    {
        Assert.AreEqual("roslyn-5.6.0", Path.GetFileName(CSharpScriptTestPaths.ShowcaseRoot));

        var hostProject = XDocument.Load(Path.Combine(CSharpScriptTestPaths.ShowcaseRoot, "host", "host.csproj"));
        var scriptingReference = PackageReference(hostProject, "Microsoft.CodeAnalysis.CSharp.Scripting");
        Assert.AreEqual(RoslynVersion, (string?)scriptingReference.Attribute("Version"));

        var testProjectPath = Path.Combine(
            CSharpScriptTestPaths.RepositoryRoot,
            "tests",
            "DotNetKnowledge.Corpus.Tests",
            "DotNetKnowledge.Corpus.Tests.csproj");
        var testProject = XDocument.Load(testProjectPath);
        var toolsetReference = PackageReference(testProject, "Microsoft.Net.Compilers.Toolset");
        Assert.AreEqual(RoslynVersion, (string?)toolsetReference.Attribute("Version"));
        Assert.AreEqual("all", (string?)toolsetReference.Attribute("PrivateAssets"));
    }

    [TestMethod]
    public void ScriptHostRemainsOutsideTheSdkTfmCorpusMatrix()
    {
        var projects = CorpusProjectDiscovery.FindSdkStyleLibraries(CSharpScriptTestPaths.RepositoryRoot);

        Assert.HasCount(11, projects);
        Assert.IsTrue(projects.All(project =>
            project.RepositoryRelativePath.StartsWith(
                "examples/language-features/CSharp/dotnet/",
                StringComparison.Ordinal)));
    }

    private static XElement PackageReference(XDocument document, string include)
    {
        var matches = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals((string?)element.Attribute("Include"), include, StringComparison.Ordinal))
            .ToArray();
        Assert.HasCount(1, matches, $"Expected exactly one PackageReference for {include}.");
        return matches[0];
    }

    private static string CanonicalContainedPath(string scenarioDirectory, string relativePath)
    {
        var canonicalRoot = Path.GetFullPath(scenarioDirectory);
        var canonicalPath = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
        var relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        Assert.IsFalse(
            Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
            $"Declared path escapes its scenario directory: {relativePath}.");
        return canonicalPath;
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
