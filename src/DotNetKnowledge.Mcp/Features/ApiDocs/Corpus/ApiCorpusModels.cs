namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public sealed record ApiCorpus(
    int SchemaVersion,
    IReadOnlyList<ApiCorpusType> Types,
    IReadOnlyList<ApiSkippedDeclaration> Skipped);

/// <summary>
/// A declaration the metadata reader could not model, recorded rather than dropped. Failing the
/// whole package on one unreadable member cost the coverage of every other member in it; skipping
/// silently would be worse still, because an absent member is indistinguishable from one that was
/// never declared. <see cref="Reason"/> is the reader's own message for the shape it met.
/// </summary>
public sealed record ApiSkippedDeclaration(
    string Kind,
    string DeclaringType,
    string? Name,
    string Reason);

public sealed record ApiCorpusType(
    string EcmaId,
    string Name,
    string FullName,
    ApiTypeUse? BaseType,
    IReadOnlyList<ApiTypeUse> Interfaces,
    IReadOnlyList<ApiTypeUse> Constraints,
    IReadOnlyList<ApiAttributeUse> Attributes,
    ApiDocumentation Documentation,
    IReadOnlyList<ApiCorpusMember> Members);

public sealed record ApiCorpusMember(
    string EcmaId,
    string Name,
    string Kind,
    string Signature,
    IReadOnlyList<ApiTypeUse> Parameters,
    ApiTypeUse? ReturnType,
    IReadOnlyList<ApiTypeUse> Constraints,
    IReadOnlyList<ApiAttributeUse> Attributes,
    ApiDocumentation Documentation);

public sealed record ApiTypeUse(
    string? Name,
    string TypeExpression,
    IReadOnlyList<string> TypeNames);

public sealed record ApiAttributeUse(
    string Application,
    string AttributeType,
    IReadOnlyList<string> ArgumentTypeNames);

public sealed record ApiDocumentation(
    string? Summary,
    IReadOnlyList<ApiNamedDocumentation> Parameters,
    IReadOnlyList<ApiNamedDocumentation> TypeParameters,
    string? Returns,
    string? Value,
    string? Remarks,
    IReadOnlyList<ApiNamedDocumentation> Exceptions);

public sealed record ApiNamedDocumentation(string Name, string Text);
