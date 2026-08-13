using System.Text.Json;
using System.Security.Cryptography;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public static class PackageApiCorpusStore
{
    private const int SchemaVersion = 1;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<CacheKey, CacheEntry> Cache = [];

    public static ApiCorpus Read(string path, ApiPackageDefinition definition, string framework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        var key = new CacheKey(definition.PackageId, definition.Version, definition.Sha512, framework);
        var bytes = File.ReadAllBytes(path);
        var stored = JsonSerializer.Deserialize<StoredCorpus>(bytes)
            ?? throw new InvalidDataException("The package corpus is empty.");
        if (stored.SchemaVersion != SchemaVersion || stored.Corpus?.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("The package corpus has an unsupported schema version.");
        if (!string.Equals(stored.PackageId, definition.PackageId, StringComparison.Ordinal)
            || !string.Equals(stored.Version, definition.Version, StringComparison.Ordinal)
            || !string.Equals(stored.Sha512, definition.Sha512, StringComparison.Ordinal)
            || !string.Equals(stored.Framework, framework, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The package corpus identity does not match the requested package framework.");
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(bytes));
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached) && cached.ContentHash == contentHash)
                return cached.Corpus;

            Cache[key] = new CacheEntry(contentHash, stored.Corpus);
            return stored.Corpus;
        }
    }

    internal static async Task WriteAsync(
        string path,
        ApiPackageDefinition definition,
        string framework,
        ApiCorpus corpus,
        CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new StoredCorpus(SchemaVersion, definition.PackageId, definition.Version, definition.Sha512,
                        framework, corpus),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static int GetCachedVariantCount(ApiPackageDefinition definition, string framework)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(framework);
        var key = new CacheKey(definition.PackageId, definition.Version, definition.Sha512, framework);
        lock (CacheLock)
            return Cache.ContainsKey(key) ? 1 : 0;
    }

    private sealed record CacheKey(string PackageId, string Version, string Sha512, string Framework);

    private sealed record CacheEntry(string ContentHash, ApiCorpus Corpus);

    private sealed record StoredCorpus(
        int SchemaVersion, string PackageId, string Version, string Sha512, string Framework, ApiCorpus? Corpus);
}
