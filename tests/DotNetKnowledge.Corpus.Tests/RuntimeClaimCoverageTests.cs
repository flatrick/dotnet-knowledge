using DotNetKnowledge.Corpus.Tests.Cases;

namespace DotNetKnowledge.Corpus.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RuntimeClaimCoverageTests
{
    private const string CSharpMarkerPrefix = "// Runtime verification: ";
    private const string VisualBasicMarkerPrefix = "' Runtime verification: ";

    [TestMethod]
    public void RuntimeCasesAndCanonicalSourceMarkersAreBijective()
    {
        var repositoryRoot = RepositoryRoot();
        var cases = LoadCases();
        var markers = FindCanonicalMarkers(repositoryRoot);

        var errors = FindCoverageErrors(cases, markers);

        Assert.HasCount(0, errors, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void DuplicateMarkersReportEveryCanonicalSourceLocation()
    {
        var testCase = new CorpusCase(
            "CSharp11.NumericIntPtr",
            "examples/language-features/CSharp/dotnet/10/latest/library/CSharp11/NumericIntPtr/NumericIntPtr.cs",
            [],
            [new RuntimeExpectation("harness.cs", "10.0", "net10.0", "11.0", 0, [])]);
        RuntimeMarker[] markers =
        [
            new("CSharp11.NumericIntPtr", "examples/language-features/First.cs", 3),
            new("CSharp11.NumericIntPtr", "examples/language-features/Second.cs", 7)
        ];

        var errors = FindCoverageErrors([new CaseDocument("NumericIntPtr.case.json", testCase)], markers);

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "examples/language-features/First.cs:3");
        StringAssert.Contains(errors[0], "examples/language-features/Second.cs:7");
    }

    [TestMethod]
    public void MarkerOnAnotherCanonicalSourceDoesNotSatisfyRuntimeCase()
    {
        const string expectedSource =
            "examples/language-features/CSharp/dotnet/10/latest/library/CSharp11/NumericIntPtr/NumericIntPtr.cs";
        var testCase = new CorpusCase(
            "CSharp11.NumericIntPtr",
            expectedSource,
            [],
            [new RuntimeExpectation("harness.cs", "10.0", "net10.0", "11.0", 0, [])]);
        var marker = new RuntimeMarker(
            "CSharp11.NumericIntPtr",
            "examples/language-features/CSharp/dotnet/10/latest/library/Unrelated.cs",
            4);

        var errors = FindCoverageErrors(
            [new CaseDocument("NumericIntPtr.case.json", testCase)],
            [marker]);

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "examples/language-features/CSharp/dotnet/10/latest/library/Unrelated.cs:4");
        StringAssert.Contains(errors[0], expectedSource);
    }

    [TestMethod]
    public void CanonicalMarkerDiscoveryRecognizesVisualBasicCommentSyntax()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-{Guid.NewGuid():N}");
        var csharpRoot = Path.Combine(
            repositoryRoot,
            "examples",
            "language-features",
            "CSharp",
            "dotnet",
            "10",
            "latest");
        var visualBasicRoot = Path.Combine(
            repositoryRoot,
            "examples",
            "language-features",
            "VB.NET",
            "dotnet",
            "Net10");
        Directory.CreateDirectory(csharpRoot);
        Directory.CreateDirectory(visualBasicRoot);

        try
        {
            var sourcePath = Path.Combine(visualBasicRoot, "RuntimeClaim.vb");
            File.WriteAllText(
                sourcePath,
                """
                Namespace RuntimeClaims
                    ' Runtime verification: Vb17_13.RuntimeClaim
                End Namespace
                """);

            var markers = FindCanonicalMarkers(repositoryRoot);

            Assert.HasCount(1, markers);
            Assert.AreEqual("Vb17_13.RuntimeClaim", markers[0].Id);
            Assert.AreEqual(
                "examples/language-features/VB.NET/dotnet/Net10/RuntimeClaim.vb",
                markers[0].Path);
            Assert.AreEqual(2, markers[0].LineNumber);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static CaseDocument[] LoadCases()
    {
        var caseDirectory = Path.Combine(AppContext.BaseDirectory, "TestCases");
        if (!Directory.Exists(caseDirectory))
        {
            throw new InvalidOperationException($"Corpus case directory does not exist: {caseDirectory}.");
        }

        var casePaths = Directory.GetFiles(caseDirectory, "*.json", SearchOption.AllDirectories);
        return CorpusCaseLoader.LoadValidated(casePaths, RepositoryRoot())
            .Select(document => new CaseDocument(document.Path, document.Case))
            .ToArray();
    }

    private static List<RuntimeMarker> FindCanonicalMarkers(string repositoryRoot)
    {
        string[] canonicalRoots =
        [
            "examples/language-features/CSharp/dotnet/10/latest",
            "examples/language-features/VB.NET/dotnet/Net10"
        ];
        var markers = new List<RuntimeMarker>();

        foreach (var relativeRoot in canonicalRoots)
        {
            var canonicalRoot = Path.Combine(
                repositoryRoot,
                relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(canonicalRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Canonical corpus source directory does not exist: {canonicalRoot}.");
            }

            foreach (var path in Directory.EnumerateFiles(canonicalRoot, "*.*", SearchOption.AllDirectories)
                         .Where(path => Path.GetExtension(path) is ".cs" or ".vb")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var markerPrefix = Path.GetExtension(path) switch
                {
                    ".cs" => CSharpMarkerPrefix,
                    ".vb" => VisualBasicMarkerPrefix,
                    _ => throw new InvalidOperationException($"Unsupported canonical source type: {path}.")
                };
                var lineNumber = 0;
                foreach (var line in File.ReadLines(path))
                {
                    lineNumber++;
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith(markerPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    markers.Add(new RuntimeMarker(
                        trimmed[markerPrefix.Length..].Trim(),
                        Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                        lineNumber));
                }
            }
        }

        return markers;
    }

    private static List<string> FindCoverageErrors(
        IReadOnlyList<CaseDocument> cases,
        IReadOnlyList<RuntimeMarker> markers)
    {
        var errors = new List<string>();
        var casesById = cases
            .GroupBy(document => document.Case.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var markersById = markers
            .GroupBy(marker => marker.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var markerGroup in markersById.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(markerGroup.Key))
            {
                errors.Add(
                    $"Runtime verification marker must name a case ID: {Locations(markerGroup.Value)}.");
                continue;
            }

            if (!casesById.TryGetValue(markerGroup.Key, out var matchingCases))
            {
                errors.Add(
                    $"Runtime verification marker '{markerGroup.Key}' has no matching case: " +
                    $"{Locations(markerGroup.Value)}.");
                continue;
            }

            if (matchingCases.Length != 1)
            {
                errors.Add(
                    $"Runtime verification marker '{markerGroup.Key}' matches {matchingCases.Length} cases: " +
                    $"{Locations(markerGroup.Value)}.");
                continue;
            }

            if (matchingCases[0].Case.Runtimes.Count == 0)
            {
                errors.Add(
                    $"Runtime verification marker '{markerGroup.Key}' matches a case without runtime expectations: " +
                    $"{Locations(markerGroup.Value)}.");
                continue;
            }

            if (markerGroup.Value.Length != 1)
            {
                continue;
            }

            var expectedSource = NormalizeRepositoryPath(matchingCases[0].Case.Source);
            var markerPaths = markerGroup.Value
                .Select(marker => NormalizeRepositoryPath(marker.Path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (markerPaths.Length != 1 ||
                !string.Equals(markerPaths[0], expectedSource, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Runtime verification marker '{markerGroup.Key}' does not belong to its case source " +
                    $"'{expectedSource}': {Locations(markerGroup.Value)}.");
            }
        }

        foreach (var caseGroup in cases
                     .Where(document =>
                         document.Case.Runtimes.Count > 0 &&
                         IsCorpusSource(document.Case.Source))
                     .GroupBy(document => document.Case.Id, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (caseGroup.Count() != 1)
            {
                errors.Add(
                    $"Runtime case ID '{caseGroup.Key}' appears in {caseGroup.Count()} case documents.");
                continue;
            }

            if (!markersById.TryGetValue(caseGroup.Key, out var matchingMarkers))
            {
                errors.Add($"Runtime case '{caseGroup.Key}' has no canonical source marker.");
                continue;
            }

            if (matchingMarkers.Length != 1)
            {
                errors.Add(
                    $"Runtime case '{caseGroup.Key}' has {matchingMarkers.Length} canonical source markers: " +
                    $"{Locations(matchingMarkers)}.");
            }
        }

        return errors;
    }

    private static bool IsCorpusSource(string path) =>
        path.Replace('\\', '/')
            .StartsWith("examples/language-features/", StringComparison.Ordinal);

    private static string NormalizeRepositoryPath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string Locations(IEnumerable<RuntimeMarker> markers) =>
        string.Join(
            ", ",
            markers
                .Select(marker => $"{marker.Path}:{marker.LineNumber}")
                .OrderBy(location => location, StringComparer.Ordinal));

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

    private sealed record CaseDocument(string Path, CorpusCase Case);

    private sealed record RuntimeMarker(string Id, string Path, int LineNumber);
}
