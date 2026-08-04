namespace DotNetKnowledge.Mcp.Sources;

public sealed record SourceSyncState(
    int SchemaVersion,
    string Name,
    string Repository,
    string Url,
    string Ref,
    string Commit,
    DateTimeOffset FetchedAt,
    IReadOnlyList<string> SparsePaths);
