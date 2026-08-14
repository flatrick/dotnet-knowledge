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
    string CorpusDirectory,
    // Absent from state written before the field existed, which deserializes to 0 and so reads as
    // stale -- exactly the signal wanted, and only for a source that actually has corpora.
    int CorpusSchemaVersion);
