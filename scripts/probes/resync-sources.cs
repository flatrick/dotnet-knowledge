#!/usr/bin/env dotnet
#:property Nullable=enable
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. It drives SourceSynchronizer.SyncAsync over every cataloged source, or the
// ones named, using THIS checkout's build.
//
// The question it answers: how do I resynchronize with code that is not installed yet? sync_source
// through an MCP client runs the user-global tool, which lags the working tree between an install
// and a client restart. Syncing that way after a corpus-format or catalog change writes the OLD
// format with the OLD catalog, which the new code then rejects as stale -- so the obvious route is
// the wrong one precisely when a resync is most needed.
//
// Wired exactly as Program.cs wires it, so the API package contributor participates and the
// supplements are built the same way a real sync builds them.
//
//   dotnet run --file scripts/probes/resync-sources.cs
//   dotnet run --file scripts/probes/resync-sources.cs -- roslyn-api-docs
//
// This DOWNLOADS, unlike every other probe here: a full generation per source, which for
// dotnet-api-docs is several hundred megabytes. Sources are synced at their pinned commits.

using System.Diagnostics;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

var catalog = new SourceCatalog("sources.json");
var cache = new SourceCache();
using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
var contributors = new ISourceGenerationContributor[]
{
    new ApiPackageGenerationContributor(
        new NuGetPackageClient(httpClient),
        new PackageApiCorpusBuilder()),
};
var synchronizer = new SourceSynchronizer(catalog, cache, contributors);

// Sources carrying API packages go first: they are the ones a corpus-format change invalidates, so
// a run that is going to fail should fail before several hundred megabytes are fetched.
var requested = args.Where(item => !item.StartsWith('-')).ToArray();
foreach (var name in requested.Where(item => !catalog.Sources.ContainsKey(item)))
{
    Console.Error.WriteLine($"Unknown source '{name}'.");
    return 2;
}

var order = (requested.Length > 0 ? requested : catalog.Sources.Keys.ToArray())
    .OrderBy(name => catalog.Sources[name].ApiPackages is { Count: > 0 } ? 0 : 1)
    .ThenBy(name => name, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine($"cache: {cache.Root}");
Console.WriteLine($"sources: {string.Join(", ", order)}");
Console.WriteLine();

var failures = 0;
foreach (var name in order)
{
    var stopwatch = Stopwatch.StartNew();
    Console.WriteLine($"=== {name} ===");
    var progress = new Progress<string>(stage => Console.WriteLine($"    {stage}"));
    try
    {
        var result = await synchronizer.SyncAsync(name, null, CancellationToken.None, progress);
        Console.WriteLine(
            $"    OK {result.Commit[..12]} in {stopwatch.Elapsed.TotalSeconds:F1}s -> {result.CacheDir}");
        foreach (var package in result.ApiPackages)
        {
            Console.WriteLine(
                $"    package {package.PackageId} {package.Version} "
                + $"[{string.Join(", ", package.AvailableFrameworks)}] default={package.DefaultFramework}");
        }
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"    FAILED after {stopwatch.Elapsed.TotalSeconds:F1}s: {exception.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine(failures == 0 ? "all sources synchronized" : $"{failures} source(s) failed");
return failures == 0 ? 0 : 1;
