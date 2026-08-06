using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

public sealed record LanguageDocLineHit(
    string Path,
    int Line,
    string Text,
    bool IsTruncated,
    string SectionPath,
    SourceProvenance Source);

public sealed record LanguageDocSearchResult(
    IReadOnlyList<LanguageDocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);

public sealed record LanguageDocContentResult(
    string Path,
    SourceProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken);

public sealed record LanguageDocOutlineEntry(int Level, string Text, string Path);

public sealed record LanguageDocOutlineResult(
    string Path,
    SourceProvenance Source,
    IReadOnlyList<LanguageDocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken);

public sealed class LanguageDocPathNotFoundException : Exception
{
    public LanguageDocPathNotFoundException(string path, string sourceName)
        : base($"'{path}' was not found in '{sourceName}'. Call search_language_docs, or list_sources for cacheDir.")
    {
        Path = path;
        SourceName = sourceName;
    }

    public string Path { get; }
    public string SourceName { get; }
}

public sealed class LanguageDocSectionNotFoundException : Exception
{
    public LanguageDocSectionNotFoundException(string section, string path, string sourceName)
        : base($"Section '{section}' was not found in '{path}' ({sourceName}). " +
               "Call get_language_doc_outline to see valid section paths for this document.")
    {
        Section = section;
        Path = path;
        SourceName = sourceName;
    }

    public string Section { get; }
    public string Path { get; }
    public string SourceName { get; }
}
