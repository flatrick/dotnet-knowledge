namespace DotNetKnowledge.Mcp.Features.ApiDocs;

public sealed record SourceProvenance(
    string Repo,
    string Ref,
    string Commit,
    DateTimeOffset FetchedAt);

public sealed record ApiParameterDocumentation(
    string Name,
    string? Description);

public sealed record ApiMemberDocumentation(
    string Name,
    string Signature,
    string? Summary,
    IReadOnlyList<ApiParameterDocumentation> Parameters,
    string? Returns,
    string? Remarks);

public sealed record ApiTypeDocumentation(
    string FullName,
    IReadOnlyList<ApiMemberDocumentation> Members,
    SourceProvenance Source);

public sealed record ApiLookupResult(
    IReadOnlyList<ApiTypeDocumentation> Matches,
    IReadOnlyList<SourceProvenance> SearchedSources);

public sealed record ApiSearchItem(
    string Name,
    SourceProvenance Source);

public sealed record ApiSearchResult(
    IReadOnlyList<ApiSearchItem> Items,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);

public sealed class SourceNotSyncedException : InvalidOperationException
{
    public SourceNotSyncedException(string sourceName, Exception? innerException = null)
        : base($"{sourceName} is not synced. Call sync_source(name: \"{sourceName}\") first.", innerException)
    {
        SourceName = sourceName;
    }

    public string SourceName { get; }
}
