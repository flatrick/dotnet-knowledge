using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Features.Docs;

public sealed record DocLineHit(
    string Path,
    int Line,
    string Text,
    bool IsTruncated,
    string SectionPath,
    GitProvenance Source,
    // Set when this hit came from a document the server rendered rather than read verbatim. The
    // line number then indexes the rendering, not the bytes on disk. It belongs per hit, not per
    // result, because an unfiltered search fans across rendered and verbatim documents at once.
    string? RenderedFrom = null);

public sealed record DocNormalizationNote(string Message);

/// <summary>
/// A document the server declined to read, and why. A dropped file is indistinguishable from one
/// with no matches, which is the failure the no-silent-absence rule exists to prevent; this is the
/// document-side counterpart of skippedDeclarations on the API payloads.
/// </summary>
public sealed record DocSkippedDocument(string Path, string Reason);

public sealed record DocSearchResult(
    IReadOnlyList<DocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<GitProvenance> SearchedSources,
    DocNormalizationNote? NormalizationNote = null,
    IReadOnlyList<DocSkippedDocument>? SkippedDocuments = null);

public sealed record DocContentResult(
    string Path,
    GitProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null,
    string? RenderedFrom = null);

public sealed record DocOutlineEntry(int Level, string Text, string Path);

public sealed record DocOutlineResult(
    string Path,
    GitProvenance Source,
    IReadOnlyList<DocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null,
    string? RenderedFrom = null);

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
