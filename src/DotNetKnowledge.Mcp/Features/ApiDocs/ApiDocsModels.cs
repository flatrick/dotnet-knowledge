using System.Text.Json.Serialization;

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
    IReadOnlyList<ApiParameterDocumentation>? Parameters,
    string? Returns,
    string? Remarks);

public sealed record ApiTypeDocumentation(
    string FullName,
    IReadOnlyList<ApiMemberDocumentation> Members,
    SourceProvenance Source);

/// <summary>
/// Why a lookup returned nothing. A type that does not exist and a member that does not exist need
/// different remedies, and only the second is answerable with another lookup.
/// </summary>
public enum ApiLookupOutcome
{
    Found,
    TypeNotFound,
    MemberNotFound,
}

public sealed record ApiLookupResult(
    IReadOnlyList<ApiTypeDocumentation> Matches,
    IReadOnlyList<SourceProvenance> SearchedSources,
    [property: JsonIgnore] ApiLookupOutcome Outcome,
    IReadOnlyList<string> ResolvedTypeNames,
    bool IsPartial,
    string? NextPageToken);

public sealed record ApiSearchItem(
    string Name,
    string MatchedOn,
    SourceProvenance Source);

/// <summary>
/// Which part of a fully-qualified name a search pattern matched. A caller that asked for a type
/// name and received a namespace's entire contents has been answered a question it did not ask, so
/// the distinction belongs in the response rather than in the caller's assumptions.
/// </summary>
public static class ApiNameMatch
{
    public const string Type = "type";
    public const string Namespace = "namespace";
    public const string FullName = "fullName";
}

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
