namespace DotNetKnowledge.Mcp.Sources;

public sealed record SourceSnapshot(
    SourceDefinition Definition,
    SourceSyncState State,
    string GenerationDirectory,
    string RepositoryDirectory,
    string SupplementsDirectory);

public interface ISourceGenerationContributor
{
    bool AppliesTo(SourceDefinition definition);

    Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
        SourceDefinition definition,
        string refLabel,
        string repositoryDirectory,
        string supplementsDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public sealed record ApiPackageSyncState(
    string PackageId,
    string AssemblyName,
    string Version,
    string Sha512,
    string Feed,
    DateTimeOffset FetchedAt,
    string DefaultFramework,
    IReadOnlyList<string> AvailableFrameworks,
    string CorpusDirectory);
