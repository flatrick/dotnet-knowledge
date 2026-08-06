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

/// <summary>
/// Which reading of the requested symbol produced a match, and therefore how much of each member
/// it carries. Without it a caller cannot tell a signatures-only answer from a signatures-only
/// decision, because the two look identical in the payload.
/// </summary>
public static class ApiLookupDetail
{
    public const string Signatures = "signatures";
    public const string Full = "full";
}

public sealed record ApiTypeDocumentation(
    string FullName,
    IReadOnlyList<ApiMemberDocumentation> Members,
    SourceProvenance Source,
    string Detail);

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

public sealed record ApiTextHit(
    string Symbol,
    string Element,
    string Text,
    bool IsTruncated,
    SourceProvenance Source);

public sealed record ApiTextSearchResult(
    IReadOnlyList<ApiTextHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);

/// <summary>
/// How a declaration uses the type asked about. These are different questions wearing one word:
/// "what accepts a CancellationToken" and "what derives from Stream" have different answers and
/// different uses, so a hit says which it is and a caller can ask for one.
/// </summary>
public static class ApiReferenceKind
{
    public const string Parameter = "parameter";
    public const string Return = "return";
    public const string Base = "base";
    public const string Interface = "interface";

    public static readonly string[] All = [Parameter, Return, Base, Interface];
}

/// <param name="IsExact">
/// Whether the declaration names the type itself rather than an expression parameterized by it.
/// A class implementing <c>IComparer&lt;string&gt;</c> is an <c>interface</c> hit for
/// <c>System.String</c>; without this, telling that from a class implementing <c>System.String</c>
/// means string-matching <see cref="TypeExpression"/> against the symbol in the caller.
/// </param>
public sealed record ApiReferenceHit(
    string Symbol,
    string Kind,
    string? ParameterName,
    string? TypeExpression,
    bool IsExact,
    string? Signature,
    SourceProvenance Source);

/// <summary>
/// Per-kind counts over the whole result set, not the page. A ubiquitous type has tens of thousands
/// of references, and paginating them twenty at a time is a way of not saying so.
/// </summary>
public sealed record ApiReferenceTotals(int Parameter, int Return, int Base, int Interface);

public sealed record ApiReferenceResult(
    IReadOnlyList<ApiReferenceHit> Hits,
    ApiReferenceTotals Totals,
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
