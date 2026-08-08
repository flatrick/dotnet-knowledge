using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Features.Docs;

public sealed record DocLineHit(
    string Path,
    int Line,
    string Text,
    bool IsTruncated,
    string SectionPath,
    SourceProvenance Source);

public sealed record DocSearchResult(
    IReadOnlyList<DocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);

public sealed record DocContentResult(
    string Path,
    SourceProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken);

public sealed record DocOutlineEntry(int Level, string Text, string Path);

public sealed record DocOutlineResult(
    string Path,
    SourceProvenance Source,
    IReadOnlyList<DocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken);

public sealed class DocPathNotFoundException : Exception
{
    public DocPathNotFoundException(string path, string sourceName)
        : base($"'{path}' was not found in '{sourceName}'. Call search_docs, or list_sources for cacheDir.")
    {
        Path = path;
        SourceName = sourceName;
    }

    public string Path { get; }
    public string SourceName { get; }
}

public sealed class DocSectionNotFoundException : Exception
{
    public DocSectionNotFoundException(string section, string path, string sourceName)
        : base($"Section '{section}' was not found in '{path}' ({sourceName}). " +
               "Call get_doc_outline to see valid section paths for this document.")
    {
        Section = section;
        Path = path;
        SourceName = sourceName;
    }

    public string Section { get; }
    public string Path { get; }
    public string SourceName { get; }
}
