namespace DotNetKnowledge.Mcp.Sources;

public sealed record SourceSyncResult(
    string Name,
    string Repository,
    string Ref,
    string Commit,
    DateTimeOffset FetchedAt,
    string CacheDir,
    IReadOnlyList<ApiPackageDefinition> ConfiguredApiPackages,
    IReadOnlyList<ApiPackageSyncState> ApiPackages);
