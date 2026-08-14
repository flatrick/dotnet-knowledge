using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GitProvenance), "git")]
[JsonDerivedType(typeof(NuGetProvenance), "nuget")]
public abstract record ApiProvenance
{
    [JsonIgnore]
    public abstract string RevisionKey { get; }
}

public sealed record GitProvenance(
    string Repo,
    string Ref,
    string Commit,
    DateTimeOffset FetchedAt) : ApiProvenance
{
    [JsonIgnore]
    public override string RevisionKey => JsonSerializer.Serialize(new { kind = "git", Repo, Ref, Commit });
}

public sealed record NuGetProvenance(
    string PackageId,
    string Version,
    string Sha512,
    string Feed,
    string Framework,
    DateTimeOffset FetchedAt) : ApiProvenance
{
    [JsonIgnore]
    public override string RevisionKey => JsonSerializer.Serialize(
        new { kind = "nuget", PackageId, Version, Sha512, Feed, Framework });
}

public sealed record ApiParameterDocumentation(
    string Name,
    string? Description);

public sealed record ApiMemberDocumentation(
    string Name,
    string Signature,
    string? Summary,
    IReadOnlyList<ApiParameterDocumentation>? Parameters,
    string? Returns,
    string? Remarks,
    ApiProvenance Source);

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
    ApiProvenance Source,
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

/// <summary>
/// How many declarations the metadata reader could not model, and why. Present only when something
/// was skipped, and null otherwise, so a complete corpus costs nothing to report.
/// </summary>
/// <remarks>
/// A skipped declaration is indistinguishable from one that was never declared unless the payload
/// says so, which is the same reason a capped result set carries <c>isPartial</c>. An agent that
/// searched an incomplete corpus and found nothing needs to know which of the two it got.
/// <para>
/// <see cref="Reasons"/> is capped, and <see cref="ReasonsArePartial"/> says when the cap dropped
/// something — <see cref="Declarations"/> is always the true total.
/// </para>
/// </remarks>
public sealed record ApiSkipCoverage(
    int Declarations,
    IReadOnlyList<ApiSkipReason> Reasons,
    bool ReasonsArePartial);

public sealed record ApiSkipReason(string Reason, int Count);

public sealed record ApiLookupResult(
    IReadOnlyList<ApiTypeDocumentation> Matches,
    IReadOnlyList<ApiProvenance> SearchedSources,
    [property: JsonIgnore] ApiLookupOutcome Outcome,
    IReadOnlyList<string> ResolvedTypeNames,
    bool IsPartial,
    string? NextPageToken,
    string? EffectiveFramework,
    string? DefaultFramework,
    IReadOnlyList<string>? AvailableFrameworks,
    ApiSkipCoverage? SkippedDeclarations);

/// <param name="NamespaceDepth">
/// How many namespace segments sit between the namespace the pattern named and the type — 0 for a
/// type declared directly in it, 1 for one a namespace below, and so on. Present only on a
/// <see cref="ApiNameMatch.Namespace"/> match, where it is the only thing separating "declared in
/// this namespace" from "declared anywhere under it".
/// </param>
public sealed record ApiSearchItem(
    string Name,
    string MatchedOn,
    int? NamespaceDepth,
    ApiProvenance Source);

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

/// <summary>
/// Guidance attached to an otherwise-empty <see cref="ApiSearchResult"/> for a dotted pattern. A
/// <c>Type.Member</c> name or a generic type's arity-less full name matches no type name and would
/// return a bare empty set — the plausible absence this server exists to avoid — so the note names
/// the calls that would actually reach what the caller asked for.
/// </summary>
public sealed record ApiSearchNote(string Message);

public sealed record ApiSearchResult(
    IReadOnlyList<ApiSearchItem> Items,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<ApiProvenance> SearchedSources,
    ApiSearchNote? Note,
    string? EffectiveFramework,
    string? DefaultFramework,
    IReadOnlyList<string>? AvailableFrameworks,
    ApiSkipCoverage? SkippedDeclarations);

/// <param name="MoreFromSymbol">
/// How many further matches this symbol had that the per-symbol cap dropped from the result set, set
/// on the last kept hit of a symbol that overflowed and null otherwise. One symbol whose every doc
/// element mentions the query would otherwise fill a page and read as "only this API mentions it";
/// the count says the rest are reachable with lookup_api on the same symbol.
/// </param>
public sealed record ApiTextHit(
    string Symbol,
    string Element,
    string Text,
    bool IsTruncated,
    ApiProvenance Source,
    int? MoreFromSymbol = null);

public sealed record ApiTextSearchResult(
    IReadOnlyList<ApiTextHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<ApiProvenance> SearchedSources,
    string? EffectiveFramework,
    string? DefaultFramework,
    IReadOnlyList<string>? AvailableFrameworks,
    ApiSkipCoverage? SkippedDeclarations);

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
    public const string Constraint = "constraint";
    public const string Attribute = "attribute";

    public static readonly string[] All = [Parameter, Return, Base, Interface, Constraint, Attribute];
}

/// <param name="AttributeType">
/// The CLR name of the attribute an <c>attribute</c> hit's application applies —
/// <c>System.ObsoleteAttribute</c> where <see cref="TypeExpression"/> carries the source spelling
/// <c>[System.Obsolete("…")]</c>. Null on every other kind, where a type expression is already the
/// identity it names.
/// </param>
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
    string? AttributeType,
    bool IsExact,
    string? Signature,
    ApiProvenance Source);

/// <summary>
/// Per-kind counts over the whole result set, not the page. A ubiquitous type has tens of thousands
/// of references, and paginating them twenty at a time is a way of not saying so.
/// </summary>
public sealed record ApiReferenceTotals(
    int Parameter,
    int Return,
    int Base,
    int Interface,
    int Constraint,
    int Attribute);

/// <summary>
/// What a query for the de-suffixed half of a colliding pair left out. C# spells an application of
/// <c>FooAttribute</c> as <c>[Foo]</c>, so where a namespace holds both types those applications
/// belong to the attribute and not to the class the caller named. Counting them under the class
/// would be a wrong answer; excluding them without saying so would be a plausible absence, which is
/// the failure this server exists to avoid.
/// </summary>
/// <param name="AttributeApplications">
/// How many applications of <paramref name="SiblingType"/> were excluded, over the whole result set
/// rather than the page.
/// </param>
public sealed record ApiReferenceNote(
    string SiblingType,
    int AttributeApplications,
    string Remedy);

public sealed record ApiReferenceResult(
    IReadOnlyList<ApiReferenceHit> Hits,
    ApiReferenceTotals Totals,
    ApiReferenceNote? Note,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<ApiProvenance> SearchedSources,
    string? EffectiveFramework,
    string? DefaultFramework,
    IReadOnlyList<string>? AvailableFrameworks,
    ApiSkipCoverage? SkippedDeclarations);

public sealed class FrameworkNotAvailableException : ArgumentException
{
    public FrameworkNotAvailableException(
        string requestedFramework,
        string defaultFramework,
        IReadOnlyList<string> availableFrameworks)
        : base(
            $"Framework '{requestedFramework}' is not available. "
                + $"Use '{defaultFramework}' or one of: {string.Join(", ", availableFrameworks)}.",
            "framework")
    {
        RequestedFramework = requestedFramework;
        DefaultFramework = defaultFramework;
        AvailableFrameworks = availableFrameworks;
    }

    public string RequestedFramework { get; }

    public string DefaultFramework { get; }

    public IReadOnlyList<string> AvailableFrameworks { get; }
}

public sealed class SourceNotSyncedException : InvalidOperationException
{
    public SourceNotSyncedException(string sourceName, Exception? innerException = null)
        : base($"{sourceName} is not synced. Call sync_source(name: \"{sourceName}\") first.", innerException)
    {
        SourceName = sourceName;
    }

    public string SourceName { get; }
}
