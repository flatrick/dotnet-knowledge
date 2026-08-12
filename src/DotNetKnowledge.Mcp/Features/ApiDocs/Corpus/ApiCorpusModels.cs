namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public sealed record ApiCorpus(int SchemaVersion, IReadOnlyList<ApiCorpusType> Types);

public sealed record ApiCorpusType(
    string EcmaId,
    string Name,
    string FullName,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
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
