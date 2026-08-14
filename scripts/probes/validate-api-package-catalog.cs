#!/usr/bin/env dotnet
#:property Nullable=enable
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. It checks every apiPackages entry in sources.json against the SHIPPED code
// that will act on it, before a sync does.
//
// The question it answers: is this catalog entry actually valid? A wrong one is expensive to find
// otherwise, because it parses, downloads, and builds a corpus, and only fails at the very end of
// sync_source. That happened: a package was added, validated through the archive reader, corpus
// builder and corpus store -- all of which passed -- and committed, then failed the resync on a
// rule none of them run.
//
// So this deliberately runs BOTH halves:
//
//   1. RoslynPackageCohort.Read, which requires every cataloged package to carry the version the
//      synchronized checkout's cohort manifest names. This is the check that was missed, and it is
//      why a package versioned independently of Roslyn cannot be cataloged at all --
//      docs/backlog/api-packages-are-limited-to-the-roslyn-cohort.md.
//   2. The package half: SHA-512 against the bytes on disk, lib assets, corpus build, corpus read
//      back, and a pairwise diff of the resulting surfaces so a new entry's framework divergence is
//      visible at review time.
//
//   dotnet run --file scripts/probes/validate-api-package-catalog.cs
//
// It never downloads. Packages are read from the machine's NuGet folder, so a package not restored
// there is reported as unvalidatable rather than fetched, and the source must already be synced for
// the cohort check to have a manifest to read.

using System.Security.Cryptography;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

var catalog = new SourceCatalog("sources.json");
var cache = new SourceCache();
var synchronizer = new SourceSynchronizer(catalog, cache);
var packagesRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var failures = 0;

foreach (var (name, definition) in catalog.Sources.OrderBy(item => item.Key, StringComparer.Ordinal))
{
    if (definition.ApiPackages is not { Count: > 0 } packages)
        continue;

    Console.WriteLine($"=== {name}: {packages.Count} package(s) ===");

    var snapshot = await synchronizer.TryGetCurrentSnapshotAsync(name, CancellationToken.None);
    if (snapshot is null)
    {
        Console.WriteLine("  cohort   : SKIPPED, source is not synchronized (run sync_source first)");
        failures++;
    }
    else
    {
        try
        {
            var cohort = RoslynPackageCohort.Read(snapshot.RepositoryDirectory, packages, pinned: true);
            Console.WriteLine($"  cohort   : OK, manifest names '{cohort}' and every entry matches");
        }
        catch (InvalidDataException exception)
        {
            Console.WriteLine($"  cohort   : FAILED -- {exception.Message}");
            failures++;
        }
    }

    foreach (var package in packages)
    {
        Console.WriteLine($"  --- {package.PackageId} {package.Version} ---");
        var nupkg = Path.Combine(
            packagesRoot,
            package.PackageId.ToLowerInvariant(),
            package.Version,
            $"{package.PackageId.ToLowerInvariant()}.{package.Version}.nupkg");
        if (!File.Exists(nupkg))
        {
            Console.WriteLine($"      package  : NOT RESTORED locally, cannot validate ({nupkg})");
            continue;
        }

        var actual = Convert.ToBase64String(SHA512.HashData(await File.ReadAllBytesAsync(nupkg)));
        if (!string.Equals(actual, package.Sha512, StringComparison.Ordinal))
        {
            Console.WriteLine($"      sha512   : MISMATCH, catalog says {package.Sha512}, bytes say {actual}");
            failures++;
            continue;
        }

        Console.WriteLine("      sha512   : matches the bytes on disk");
        var output = Path.Combine(Path.GetTempPath(), $"dnk-validate-{Guid.NewGuid():N}");
        try
        {
            var assets = PackageArchiveReader.ReadAssets(nupkg, package);
            var result = await new PackageApiCorpusBuilder()
                .BuildAsync(nupkg, package, output, CancellationToken.None);
            if (!result.AvailableFrameworks.Contains(package.DefaultFramework, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"      default  : FAILED -- '{package.DefaultFramework}' is not among "
                    + string.Join(", ", result.AvailableFrameworks));
                failures++;
            }

            var surfaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var framework in result.AvailableFrameworks)
            {
                var corpus = PackageApiCorpusStore.Read(result.CorpusFiles[framework], package, framework);
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in corpus.Types)
                {
                    identities.Add(type.EcmaId);
                    foreach (var member in type.Members)
                        identities.Add(member.EcmaId);
                }

                surfaces[framework] = identities;
                var attributeSkips = corpus.Skipped.Count(item =>
                    string.Equals(item.Kind, "attribute", StringComparison.Ordinal));
                Console.WriteLine(
                    $"      {framework,-8} types={corpus.Types.Count,4} members={corpus.Types.Sum(t => t.Members.Count),5} "
                    + $"declarations-lost={corpus.Skipped.Count - attributeSkips} attributes-dropped={attributeSkips}");
            }

            // Divergence is not a defect. It is reported because it is the whole reason the corpus
            // is stored per framework, and a reviewer should see whether a new entry exercises that.
            var frameworks = surfaces.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var diverged = false;
            for (var left = 0; left < frameworks.Length; left++)
            {
                for (var right = left + 1; right < frameworks.Length; right++)
                {
                    var onlyLeft = surfaces[frameworks[left]].Except(surfaces[frameworks[right]]).Count();
                    var onlyRight = surfaces[frameworks[right]].Except(surfaces[frameworks[left]]).Count();
                    if (onlyLeft + onlyRight == 0)
                        continue;

                    diverged = true;
                    Console.WriteLine(
                        $"      diverges : {frameworks[left]} vs {frameworks[right]} "
                        + $"(only-{frameworks[left]}={onlyLeft}, only-{frameworks[right]}={onlyRight})");
                }
            }

            if (!diverged && frameworks.Length > 1)
                Console.WriteLine($"      diverges : no, all {frameworks.Length} frameworks have one surface");
        }
        catch (InvalidDataException exception)
        {
            Console.WriteLine($"      corpus   : FAILED -- {exception.Message}");
            failures++;
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    Console.WriteLine();
}

Console.WriteLine(failures == 0 ? "catalog is valid" : $"{failures} problem(s) found");
return failures == 0 ? 0 : 1;
