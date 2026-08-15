using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetKnowledge.Markdown;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;
using DotNetKnowledge.Mcp.Text;
using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Mcp.Features.Docs;

public sealed class DocsQueryService
{
    /// <summary>
    /// Characters of a matched line a search hit carries — the same budget the API search uses, so
    /// the two tools cap and report alike.
    /// </summary>
    private const int MatchTextBudget = 300;

    private readonly SourceCatalog _catalog;
    private readonly SourceSynchronizer _synchronizer;

    public DocsQueryService(SourceCatalog catalog, SourceCache cache, SourceSynchronizer synchronizer)
    {
        _catalog = catalog;
        ArgumentNullException.ThrowIfNull(cache);
        _synchronizer = synchronizer;
    }

    public async Task<DocOutlineResult> GetOutlineAsync(
        string path,
        string source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 500.");

        var (text, provenance, resolvedPath, note, renderedFrom) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var headings = MarkdownOutline.Extract(text);

        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, resolvedPath);
        var offset = DecodeCursor(cursor, "lang-outline", scope, revisions);
        if (offset > headings.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = headings.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < headings.Count;

        return new DocOutlineResult(
            resolvedPath,
            provenance,
            page.Select(heading => new DocOutlineEntry(heading.Level, heading.Text, heading.Path)).ToArray(),
            isPartial,
            isPartial ? EncodeCursor("lang-outline", scope, nextOffset, revisions) : null,
            note,
            renderedFrom);
    }

    public async Task<DocSearchResult> SearchAsync(
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

        // Validate once, up front, and keep the built Regex: an invalid pattern must fail the same
        // way regardless of how many markdown files a source happens to hold, and every source's
        // scan reuses this one instance instead of rebuilding it per file.
        var compiledPattern = regex ? new Regex(query, RegexOptions.NonBacktracking) : null;

        var sourceNames = ResolveSourceNames(source);
        var (hits, searchedSources, skipped) = await CollectHitsAsync(
            query, compiledPattern, sourceNames, cancellationToken).ConfigureAwait(false);

        var effectiveQuery = query;
        DocNormalizationNote? note = null;
        if (hits.Count == 0 && !regex
            && CallerInputNormalization.TryNormalize(query, out var normalizedQuery)
            && !string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var (normalizedHits, normalizedSearchedSources, normalizedSkipped) = await CollectHitsAsync(
                normalizedQuery, compiledPattern: null, sourceNames, cancellationToken).ConfigureAwait(false);
            if (normalizedHits.Count > 0)
            {
                hits = normalizedHits;
                searchedSources = normalizedSearchedSources;
                skipped = normalizedSkipped;
                effectiveQuery = normalizedQuery;
                note = new DocNormalizationNote(
                    $"No literal match for '{query}'; results reflect the HTML-entity/typography-" +
                    $"normalized form '{normalizedQuery}'.");
            }
        }

        var ordered = DocRanking.Order(hits, effectiveQuery);

        var revisions = searchedSources.Select(RevisionKey).ToArray();
        var scope = EncodeScope(effectiveQuery, regex, source ?? string.Empty);
        var offset = DecodeCursor(cursor, "lang-search", scope, revisions);
        if (offset > ordered.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Count;

        return new DocSearchResult(
            page,
            isPartial,
            isPartial ? EncodeCursor("lang-search", scope, nextOffset, revisions) : null,
            searchedSources,
            note,
            skipped.Count > 0 ? skipped : null);
    }

    private async Task<(List<DocLineHit> Hits, List<GitProvenance> SearchedSources, List<DocSkippedDocument> Skipped)>
        CollectHitsAsync(
            string query, Regex? compiledPattern, string[] sourceNames, CancellationToken cancellationToken)
    {
        var hits = new List<DocLineHit>();
        var searchedSources = new List<GitProvenance>();
        var skipped = new List<DocSkippedDocument>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceSearchRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    snapshot => ReadSearchSource(
                        snapshot.RepositoryDirectory,
                        snapshot.Definition,
                        snapshot.State,
                        query,
                        compiledPattern,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Hits);
            skipped.AddRange(read.Skipped);
        }

        return (hits, searchedSources, skipped);
    }

    public async Task<DocContentResult> GetDocAsync(
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

        var (text, provenance, resolvedPath, pathNote, renderedFrom) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        int rangeStart;
        int rangeEndExclusive;
        string? resolvedSection = section;
        DocNormalizationNote? sectionNote = null;
        if (section is not null)
        {
            var headings = MarkdownOutline.Extract(text);
            var heading = headings.FirstOrDefault(
                candidate => string.Equals(candidate.Path, section, StringComparison.Ordinal));
            if (heading is null && CallerInputNormalization.TryNormalize(section, out var normalizedSection))
            {
                heading = headings.FirstOrDefault(
                    candidate => string.Equals(candidate.Path, normalizedSection, StringComparison.Ordinal));
                if (heading is not null)
                {
                    sectionNote = new DocNormalizationNote(
                        $"No section matched '{section}' exactly; resolved to '{heading.Path}' after " +
                        "decoding HTML entities and typographic characters in the section path.");
                }
            }

            if (heading is null)
                throw new DocSectionNotFoundException(section, resolvedPath, source);

            resolvedSection = heading.Path;
            rangeStart = heading.StartLine;
            rangeEndExclusive = heading.EndLine;
        }
        else
        {
            // Front matter is metadata about the document, not part of it, and search does not
            // return hits inside it either.
            rangeStart = MarkdownFrontMatter.BodyStartLine(text);
            rangeEndExclusive = lines.Length + 1;
        }

        // MarkdownOutline.Extract (above, for a sectioned fetch) and MarkdownAtomicBlocks.Find
        // (here) each run their own full Markdig parse of the same document. Sharing one parse
        // across both would mean exposing Markdig's MarkdownDocument in this library's public
        // surface, which cuts against its "input is markdown text, output is plain data" design
        // (docs/decisions.md); left as a follow-up rather than done in this fix wave.
        var atomicBlocks = MarkdownAtomicBlocks.Find(text);
        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, resolvedPath, resolvedSection ?? string.Empty);
        var decodedStartLine = DecodeCursor(cursor, "lang-doc", scope, revisions);
        // DecodeCursor's own "no cursor" sentinel is 0, an item-count offset that only makes sense
        // for "lang-outline"/"lang-search" cursors; for "lang-doc", Offset is a 1-based line number
        // instead, and 0 is never a valid line. So a null cursor takes the range's own start line
        // here rather than trusting the decoded 0 sentinel.
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        // Only a supplied cursor can fall outside the range; with none, startLine is the range's own
        // start. A document that is entirely front matter has an empty range, and must page to empty
        // text rather than report a cursor error to a caller who sent no cursor.
        if (cursor is not null && (startLine < rangeStart || startLine >= rangeEndExclusive))
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine, rangeEndExclusive, limit);
        var pageText = string.Join('\n', lines[(startLine - 1)..(endLineExclusive - 1)]);

        return new DocContentResult(
            resolvedPath,
            provenance,
            resolvedSection,
            pageText,
            startLine,
            endLineExclusive - 1,
            isPartial,
            isPartial ? EncodeCursor("lang-doc", scope, endLineExclusive, revisions) : null,
            CombineNotes(pathNote, sectionNote),
            renderedFrom);
    }

    private static DocNormalizationNote? CombineNotes(DocNormalizationNote? first, DocNormalizationNote? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return new DocNormalizationNote(first.Message + " " + second.Message);
    }

    private async Task<(string Text, GitProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note, string? RenderedFrom)>
        ReadDocumentAsync(string source, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadDocumentAttemptAsync(source, path, cancellationToken).ConfigureAwait(false);
        }
        catch (DocPathNotFoundException) when (CallerInputNormalization.TryNormalize(path, out var normalizedPath))
        {
            try
            {
                var (text, provenance, resolvedPath, _, renderedFrom) =
                    await ReadDocumentAttemptAsync(source, normalizedPath, cancellationToken).ConfigureAwait(false);
                var note = new DocNormalizationNote(
                    $"'{path}' was not found; resolved to '{resolvedPath}' after decoding HTML entities and " +
                    "typographic characters in the path.");
                return (text, provenance, resolvedPath, note, renderedFrom);
            }
            catch (Exception retryFailure) when (retryFailure is DocPathNotFoundException or ArgumentException)
            {
                // The retry failed too - either the normalized path still doesn't resolve
                // (DocPathNotFoundException), or normalization produced something Path.GetFullPath
                // itself rejects, e.g. a NUL character decoded from "&#0;" (ArgumentException).
                // Either way, report the exception for what the caller actually sent, not the
                // internally-decoded guess that also failed - matching how DocSectionNotFoundException
                // below reports the caller's raw `section`, not `normalizedSection`.
                throw new DocPathNotFoundException(path, source);
            }
        }
    }

    private async Task<(string Text, GitProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note, string? RenderedFrom)>
        ReadDocumentAttemptAsync(string source, string path, CancellationToken cancellationToken)
    {
        DocumentRead read;
        try
        {
            read = await _synchronizer.ReadCurrentSourceAsync(
                source,
                snapshot => ReadDocument(
                    snapshot.RepositoryDirectory,
                    source,
                    path,
                    snapshot.Definition,
                    snapshot.State),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw new SourceNotSyncedException(source, exception);
        }

        return (read.Text, read.Provenance, path, null, read.RenderedFrom);
    }

    /// <summary>
    /// Extensions a document may have. .yaml is accepted although no source carries one today: the
    /// content marker is what decides, so listing it costs nothing and avoids a false absence.
    /// </summary>
    private static readonly string[] DocumentExtensions = [".md", ".yml", ".yaml"];

    private sealed record RenderedDocument(string Text, string? RenderedFrom);

    private static bool HasDocumentExtension(string path) =>
        DocumentExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsYamlPath(string path) =>
        path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one place a non-markdown document becomes markdown. Returns null when the file is YAML
    /// this server does not serve - a pipeline definition or a navigation file - which is not an
    /// error. Throws <see cref="FaqParseException"/> when a file claims the FAQ schema and then
    /// cannot be read as one, which is.
    /// </summary>
    private static RenderedDocument? RenderIfServable(string fullPath, string text)
    {
        if (!IsYamlPath(fullPath))
            return new RenderedDocument(text, null);

        if (!string.Equals(LearnYamlMime.Detect(text), LearnYamlMime.Faq, StringComparison.Ordinal))
            return null;

        return new RenderedDocument(
            FaqMarkdown.Render(FaqDocument.Parse(text)),
            $"YamlMime:{LearnYamlMime.Faq}");
    }

    private sealed record DocumentRead(string Text, string? RenderedFrom, GitProvenance Provenance);

    private static DocumentRead ReadDocument(
        string directory, string source, string path, SourceDefinition definition, SourceSyncState state)
    {
        var fullPath = ResolveFullPath(directory, source, path);
        var rendered = RenderIfServable(fullPath, File.ReadAllText(fullPath));

        // YAML this server does not serve is not a document, and the caller must not be able to
        // tell it apart from a path that does not exist.
        if (rendered is null)
            throw new DocPathNotFoundException(path, source);

        return new DocumentRead(rendered.Text, rendered.RenderedFrom, ToProvenance(definition, state));
    }

    /// <summary>
    /// Enumerated per extension rather than with a single wildcard, so a source's non-document
    /// ballast - nuget-docs checks out 14 MB of images - is never walked. The extension is
    /// re-checked because a Windows search pattern also matches 8.3 short names.
    /// </summary>
    private static IEnumerable<string> EnumerateDocumentFiles(string fullRoot) =>
        DocumentExtensions
            .SelectMany(extension => Directory.EnumerateFiles(fullRoot, $"*{extension}", SearchOption.AllDirectories))
            .Where(HasDocumentExtension);

    private sealed record SourceSearchRead(
        GitProvenance Provenance,
        IReadOnlyList<DocLineHit> Hits,
        IReadOnlyList<DocSkippedDocument> Skipped);

    private static SourceSearchRead ReadSearchSource(
        string directory,
        SourceDefinition definition,
        SourceSyncState state,
        string query,
        Regex? compiledPattern,
        CancellationToken cancellationToken)
    {
        var provenance = ToProvenance(definition, state);
        var fullRoot = Path.GetFullPath(directory);
        var hits = new List<DocLineHit>();
        var skipped = new List<DocSkippedDocument>();

        foreach (var file in EnumerateDocumentFiles(fullRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');

            RenderedDocument? document;
            try
            {
                // Rendering happens before the prefilter so the prefilter tests the same text the
                // matcher will. Testing raw YAML would skip a file whose only match is a word the
                // rendering produces - the same silent absence, one layer down.
                document = RenderIfServable(file, File.ReadAllText(file));
            }
            catch (FaqParseException exception)
            {
                // A dropped file is indistinguishable from one with no matches. One unreadable
                // document must not fail a fan-out across every source, so it is named instead.
                skipped.Add(new DocSkippedDocument(relativePath, exception.Message));
                continue;
            }

            // YAML this server does not serve. Not an absence worth reporting: it was never a
            // document, and naming every pipeline definition would bury the ones that matter.
            if (document is null)
                continue;

            var text = document.Text;

            // Skip the full Markdig parse entirely for a file that cannot match: a source can hold
            // hundreds of documents, and most queries match none of them. This must check
            // per-line, the same way MarkdownLineSearch.Search below actually matches: an anchored
            // pattern like "^## " behaves differently against a single line than against the whole
            // file text (^ without RegexOptions.Multiline only matches offset 0 of whatever string
            // it's given), so a whole-file check would wrongly skip files whose only match isn't on
            // line 1.
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            var mightMatch = compiledPattern is not null
                ? lines.Any(compiledPattern.IsMatch)
                : lines.Any(line => line.Contains(query, StringComparison.Ordinal));
            if (!mightMatch)
                continue;

            var outline = MarkdownOutline.Extract(text);

            foreach (var hit in MarkdownLineSearch.Search(text, outline, query, compiledPattern))
            {
                var (matchedText, isTruncated) = DocumentationText.Budget(hit.Text, MatchTextBudget);
                hits.Add(new DocLineHit(
                    relativePath, hit.Line, matchedText, isTruncated, hit.SectionPath, provenance,
                    document.RenderedFrom));
            }
        }

        return new SourceSearchRead(provenance, hits, skipped);
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
            || !HasDocumentExtension(candidate)
            || !File.Exists(candidate))
        {
            throw new DocPathNotFoundException(path, source);
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

        return _catalog.Sources
            .Where(entry => entry.Value.Markdown)
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private void ValidateSource(string source)
    {
        if (!_catalog.Sources.TryGetValue(source, out var definition) || !definition.Markdown)
        {
            throw new ArgumentException(
                "source must name a markdown-searchable source (see the \"markdown\" field in sources.json).",
                nameof(source));
        }
    }

    private static GitProvenance ToProvenance(SourceDefinition definition, SourceSyncState state) =>
        new(definition.Repository, state.Ref, state.Commit, state.FetchedAt);

    private static string RevisionKey(GitProvenance provenance) => provenance.RevisionKey;

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
