using System.Text;
using System.Text.Json;
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
