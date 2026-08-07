namespace DotNetKnowledge.Corpus.Tests.Cases;

internal enum BuildOutcome
{
    Success,
    Failure
}

internal sealed record CorpusCase(
    string Id,
    string Source,
    IReadOnlyList<CompilationExpectation> Compilations,
    IReadOnlyList<RuntimeExpectation> Runtimes,
    IReadOnlyList<string> ProjectReferences = null!)
{
    public IReadOnlyList<string> ProjectReferences { get; init; } = ProjectReferences ?? [];

    public IReadOnlyList<string> Validate(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("Case ID is required.");
        }

        if (!File.Exists(Path.Combine(repositoryRoot, Source)))
        {
            errors.Add($"Source does not exist: {Source}.");
        }

        foreach (var projectReference in ProjectReferences)
        {
            if (!File.Exists(Path.Combine(repositoryRoot, projectReference)))
            {
                errors.Add($"Project reference does not exist: {projectReference}.");
            }
        }

        var compilationCoordinates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var compilation in Compilations)
        {
            var coordinate = CompilationCoordinate(compilation);
            if (!compilationCoordinates.Add(coordinate))
            {
                errors.Add($"Duplicate compilation coordinate: {coordinate}.");
            }

            if (compilation.Outcome == BuildOutcome.Failure && compilation.Diagnostics.Count == 0)
            {
                errors.Add($"Failure compilation {coordinate} must name at least one diagnostic.");
            }

            if (compilation.Outcome == BuildOutcome.Failure &&
                compilation.Diagnostics.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Failure compilation {coordinate} must not name blank diagnostics.");
            }

            if (compilation.Outcome == BuildOutcome.Success && compilation.Diagnostics.Count > 0)
            {
                errors.Add($"Success compilation {coordinate} must not name diagnostics.");
            }
        }

        foreach (var runtime in Runtimes)
        {
            if (!File.Exists(Path.Combine(repositoryRoot, runtime.Harness)))
            {
                errors.Add($"Harness does not exist: {runtime.Harness}.");
            }

            var coordinate = RuntimeCoordinate(runtime);
            var hasSuccessfulCompilation = Compilations.Any(compilation =>
                compilation.Outcome == BuildOutcome.Success &&
                CompilationCoordinate(compilation) == coordinate);
            if (!hasSuccessfulCompilation)
            {
                errors.Add($"Runtime coordinate {coordinate} must have a successful compilation expectation.");
            }
        }

        return errors;
    }

    private static string CompilationCoordinate(CompilationExpectation compilation) =>
        $"{compilation.SdkBand}|{compilation.TargetFramework}|{compilation.LanguageVersion}";

    private static string RuntimeCoordinate(RuntimeExpectation runtime) =>
        $"{runtime.SdkBand}|{runtime.TargetFramework}|{runtime.LanguageVersion}";
}

internal sealed record CompilationExpectation(
    string SdkBand,
    string TargetFramework,
    string LanguageVersion,
    BuildOutcome Outcome,
    IReadOnlyList<string> Diagnostics);

internal sealed record RuntimeExpectation(
    string Harness,
    string SdkBand,
    string TargetFramework,
    string LanguageVersion,
    int ExitCode,
    IReadOnlyList<string> StandardOutput);
