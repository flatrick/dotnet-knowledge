using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

public sealed record LanguageDocLineHit(
    string Path,
    int Line,
    string Text,
    string SectionPath,
    SourceProvenance Source);

public sealed record LanguageDocSearchResult(
    IReadOnlyList<LanguageDocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);

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
