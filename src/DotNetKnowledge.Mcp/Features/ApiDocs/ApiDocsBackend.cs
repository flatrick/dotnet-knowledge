namespace DotNetKnowledge.Mcp.Features.ApiDocs;

internal interface IApiDocsBackend
{
    ApiLookupRead Lookup(string symbol, CancellationToken cancellationToken);

    ApiSearchRead Search(string pattern, CancellationToken cancellationToken);

    ApiTextRead SearchText(string query, CancellationToken cancellationToken);

    ApiReferenceRead FindReferences(string symbol, CancellationToken cancellationToken);
}

internal sealed record ApiQueryCoverage(
    IReadOnlyList<ApiProvenance> SearchedSources,
    string? EffectiveFramework,
    string? DefaultFramework,
    IReadOnlyList<string>? AvailableFrameworks,
    ApiSkipCoverage? SkippedDeclarations = null);

internal sealed record ApiLookupRead(
    IReadOnlyList<ApiLookupTypeRead> Matches,
    IReadOnlyList<ApiLookupTypeRead> ResolvedTypes,
    IReadOnlyList<string> ResolvedTypeNames,
    ApiQueryCoverage Coverage);

internal sealed record ApiLookupTypeRead(
    string DeclarationId,
    ApiTypeDocumentation Documentation,
    IReadOnlyList<ApiLookupMemberRead> Members)
{
    public string FullName => Documentation.FullName;
    public ApiProvenance Source => Documentation.Source;
    public string Detail => Documentation.Detail;
}

internal sealed record ApiLookupMemberRead(
    string DeclarationId,
    ApiMemberDocumentation Documentation)
{
    public string Name => Documentation.Name;
    public string Signature => Documentation.Signature;
}

internal sealed record ApiSearchRead(
    IReadOnlyList<ApiSearchItem> Items,
    ApiQueryCoverage Coverage);

internal sealed record ApiTextRead(
    IReadOnlyList<ApiTextHitRead> Hits,
    ApiQueryCoverage Coverage);

internal sealed record ApiTextHitRead(string DeclarationId, ApiTextHit Hit)
{
    public string Symbol => Hit.Symbol;
    public string Element => Hit.Element;
    public string Text => Hit.Text;
    public bool IsTruncated => Hit.IsTruncated;
    public ApiProvenance Source => Hit.Source;
    public int? MoreFromSymbol => Hit.MoreFromSymbol;
}

internal sealed record ApiReferenceRead(
    IReadOnlyList<ApiReferenceHitRead> Items,
    string? SiblingType,
    int SiblingApplications,
    ApiQueryCoverage Coverage);

internal sealed record ApiReferenceHitRead(string DeclarationId, ApiReferenceHit Hit)
{
    public string Symbol => Hit.Symbol;
    public string Kind => Hit.Kind;
    public string? ParameterName => Hit.ParameterName;
    public string? TypeExpression => Hit.TypeExpression;
    public string? AttributeType => Hit.AttributeType;
    public bool IsExact => Hit.IsExact;
    public string? Signature => Hit.Signature;
    public ApiProvenance Source => Hit.Source;
}
