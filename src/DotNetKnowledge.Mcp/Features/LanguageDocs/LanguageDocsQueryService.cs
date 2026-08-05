using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetKnowledge.Markdown;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

public sealed class LanguageDocsQueryService
{
    private static readonly string[] SupportedSources = ["csharplang", "vblang"];

    private readonly SourceCatalog _catalog;
    private readonly SourceSynchronizer _synchronizer;

    public LanguageDocsQueryService(SourceCatalog catalog, SourceCache cache, SourceSynchronizer synchronizer)
    {
        _catalog = catalog;
        ArgumentNullException.ThrowIfNull(cache);
        _synchronizer = synchronizer;
    }

    public async Task<LanguageDocOutlineResult> GetOutlineAsync(
        string path,
        string source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 500.");

        var (text, provenance) = await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var headings = MarkdownOutline.Extract(text);

        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, path);
        var offset = DecodeCursor(cursor, "lang-outline", scope, revisions);
        if (offset > headings.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = headings.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < headings.Count;

        return new LanguageDocOutlineResult(
            path,
            provenance,
            page.Select(heading => new LanguageDocOutlineEntry(heading.Level, heading.Text, heading.Path)).ToArray(),
            isPartial,
            isPartial ? EncodeCursor("lang-outline", scope, nextOffset, revisions) : null);
    }

    public async Task<LanguageDocSearchResult> SearchAsync(
        string query,
        bool regex,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        // Validate once, up front: an invalid pattern must fail the same way regardless of how
        // many markdown files a source happens to hold, rather than only surfacing on whichever
        // file MarkdownLineSearch happens to reach first.
        if (regex)
            _ = new Regex(query, RegexOptions.NonBacktracking);

        var sourceNames = ResolveSourceNames(source);
        var hits = new List<LanguageDocLineHit>();
        var searchedSources = new List<SourceProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceSearchRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadSearchSource(directory, definition, state, query, regex),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Hits);
        }

        var ordered = hits
            .OrderBy(hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(hit => hit.Line)
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();

        var revisions = searchedSources.Select(RevisionKey).ToArray();
        var scope = EncodeScope(query, regex, source ?? string.Empty);
        var offset = DecodeCursor(cursor, "lang-search", scope, revisions);
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;

        return new LanguageDocSearchResult(
            page,
            isPartial,
            isPartial ? EncodeCursor("lang-search", scope, nextOffset, revisions) : null,
            searchedSources);
    }

    public async Task<LanguageDocContentResult> GetDocAsync(
        string path,
        string source,
        string? section,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1000 or > 50000)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1000 and 50000.");

        var (text, provenance) = await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        int rangeStart;
        int rangeEndExclusive;
        if (section is not null)
        {
            var heading = MarkdownOutline.Extract(text)
                .FirstOrDefault(candidate => string.Equals(candidate.Path, section, StringComparison.Ordinal));
            if (heading is null)
                throw new LanguageDocSectionNotFoundException(section, path, source);
            rangeStart = heading.StartLine;
            rangeEndExclusive = heading.EndLine;
        }
        else
        {
            rangeStart = 1;
            rangeEndExclusive = lines.Length + 1;
        }

        var atomicBlocks = MarkdownAtomicBlocks.Find(text);
        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, path, section ?? string.Empty);
        var decodedStartLine = DecodeCursor(cursor, "lang-doc", scope, revisions);
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        if (startLine < rangeStart || startLine >= rangeEndExclusive)
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine, rangeEndExclusive, limit);
        var pageText = string.Join('\n', lines[(startLine - 1)..(endLineExclusive - 1)]);

        return new LanguageDocContentResult(
            path,
            provenance,
            section,
            pageText,
            startLine,
            endLineExclusive - 1,
            isPartial,
            isPartial ? EncodeCursor("lang-doc", scope, endLineExclusive, revisions) : null);
    }

    private async Task<(string Text, SourceProvenance Provenance)> ReadDocumentAsync(
        string source, string path, CancellationToken cancellationToken)
    {
        DocumentRead read;
        try
        {
            read = await _synchronizer.ReadCurrentSourceAsync(
                source,
                (definition, state, directory) => ReadDocument(directory, source, path, definition, state),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw new SourceNotSyncedException(source, exception);
        }

        return (read.Text, read.Provenance);
    }

    private sealed record DocumentRead(string Text, SourceProvenance Provenance);

    private static DocumentRead ReadDocument(
        string directory, string source, string path, SourceDefinition definition, SourceSyncState state)
    {
        var fullPath = ResolveFullPath(directory, source, path);
        return new DocumentRead(File.ReadAllText(fullPath), ToProvenance(definition, state));
    }

    private sealed record SourceSearchRead(SourceProvenance Provenance, IReadOnlyList<LanguageDocLineHit> Hits);

    private static SourceSearchRead ReadSearchSource(
        string directory, SourceDefinition definition, SourceSyncState state, string query, bool regex)
    {
        var provenance = ToProvenance(definition, state);
        var fullRoot = Path.GetFullPath(directory);
        var hits = new List<LanguageDocLineHit>();

        foreach (var file in Directory.EnumerateFiles(fullRoot, "*.md", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var outline = MarkdownOutline.Extract(text);
            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');

            foreach (var hit in MarkdownLineSearch.Search(text, outline, query, regex))
            {
                var truncated = hit.Text.Length > 300 ? hit.Text[..300] + "…" : hit.Text;
                hits.Add(new LanguageDocLineHit(relativePath, hit.Line, truncated, hit.SectionPath, provenance));
            }
        }

        return new SourceSearchRead(provenance, hits);
    }

    private static string ResolveFullPath(string directory, string source, string path)
    {
        var fullRoot = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, path));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || !candidate.StartsWith(rootPrefix, comparison)
            || !File.Exists(candidate))
        {
            throw new LanguageDocPathNotFoundException(path, source);
        }

        return candidate;
    }

    private string[] ResolveSourceNames(string? source)
    {
        if (source is not null)
        {
            ValidateSource(source);
            return [source];
        }

        return SupportedSources
            .Where(_catalog.Sources.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private void ValidateSource(string source)
    {
        if (!SupportedSources.Contains(source, StringComparer.Ordinal) || !_catalog.Sources.ContainsKey(source))
            throw new ArgumentException("source must be \"csharplang\" or \"vblang\".", nameof(source));
    }

    private static SourceProvenance ToProvenance(SourceDefinition definition, SourceSyncState state) =>
        new(definition.Repository, state.Ref, state.Commit, state.FetchedAt);

    private static string RevisionKey(SourceProvenance provenance) =>
        provenance.Repo + "@" + provenance.Ref + "@" + provenance.Commit;

    private static string EncodeScope(params object[] values) => JsonSerializer.Serialize(values);

    private static string EncodeCursor(string kind, string scope, int offset, IReadOnlyList<string> revisions)
    {
        var json = JsonSerializer.Serialize(new PageCursor(Version: 1, Kind: kind, Scope: scope, Offset: offset, Revisions: revisions));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursor(string? cursor, string kind, string scope, IReadOnlyList<string> revisions)
    {
        if (cursor is null)
            return 0;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<PageCursor>(
                Encoding.UTF8.GetString(Convert.FromBase64String(base64)));

            if (decoded is null
                || decoded.Version != 1
                || decoded.Offset < 0
                || !string.Equals(decoded.Kind, kind, StringComparison.Ordinal)
                || !string.Equals(decoded.Scope, scope, StringComparison.Ordinal)
                || decoded.Revisions is null
                || !decoded.Revisions.SequenceEqual(revisions, StringComparer.Ordinal))
            {
                throw new ArgumentException("cursor does not match this request.", nameof(cursor));
            }

            return decoded.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("cursor is invalid.", nameof(cursor), exception);
        }
    }

    private sealed record PageCursor(int Version, string Kind, string Scope, int Offset, IReadOnlyList<string> Revisions);
}
