#!/usr/bin/env dotnet
#:property Nullable=enable
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. diff-tfm-surface.cs answers "does this package's public surface differ between
// its target frameworks?" for ONE package the operator names. This asks it of every package in a
// package folder tree at once, and reports the ones that diverge.
//
// The question it answers: which packages would exercise the `framework` argument at all? Every
// Roslyn package has one surface across all its frameworks, which made the argument look
// unnecessary until a wider sample showed Roslyn was the exception. Use it before cataloging a
// package, to know whether its frameworks are worth storing separately.
//
// Type-forwarding facades are counted and excluded: a framework whose assembly declares nothing has
// its API in the platform there, which is real divergence of a degenerate kind that would answer
// the question with a case nobody queries.
//
// Compares the set of public type and member ECMA identities per lib/<tfm>/<assembly>.dll, using
// the SHIPPED reader, so what it compares is what the server would store.
//
// It reads whatever the machine happens to have restored, so the rate is a property of that sample
// and not of NuGet as a whole. It never downloads and never loads an assembly for execution.

using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

var root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
for (var index = 0; index < args.Length - 1; index++)
{
    if (string.Equals(args[index], "--root", StringComparison.Ordinal))
        root = args[index + 1];
}

var findings = new List<(string Package, string Assembly, int Frameworks, int Shared, int Unique, string Detail)>();
var comparedPackages = 0;
var identicalPackages = 0;
var facades = 0;

foreach (var packageDirectory in Directory.EnumerateDirectories(root).OrderBy(item => item, StringComparer.Ordinal))
{
    var packageId = Path.GetFileName(packageDirectory);
    foreach (var versionDirectory in Directory.EnumerateDirectories(packageDirectory))
    {
        var libraryRoot = Path.Combine(versionDirectory, "lib");
        if (!Directory.Exists(libraryRoot))
            continue;

        // assembly name -> framework -> identities
        var byAssembly = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var frameworkDirectory in Directory.EnumerateDirectories(libraryRoot))
        {
            var framework = Path.GetFileName(frameworkDirectory);
            foreach (var assemblyPath in Directory.EnumerateFiles(frameworkDirectory, "*.dll"))
            {
                if (assemblyPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                try
                {
                    using var stream = File.OpenRead(assemblyPath);
                    var corpus = MetadataApiReader.Read(stream);
                    var identities = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var type in corpus.Types)
                    {
                        identities.Add(type.EcmaId);
                        foreach (var member in type.Members)
                            identities.Add(member.EcmaId);
                    }

                    if (!byAssembly.TryGetValue(assemblyName, out var perFramework))
                        byAssembly[assemblyName] = perFramework = new(StringComparer.OrdinalIgnoreCase);
                    perFramework[framework] = identities;
                }
                catch (Exception exception) when (exception is InvalidDataException or BadImageFormatException or IOException)
                {
                    // A package that cannot be read cannot answer this question either way.
                }
            }
        }

        foreach (var (assemblyName, perFramework) in byAssembly)
        {
            if (perFramework.Count < 2)
                continue;

            comparedPackages++;
            var shared = perFramework.Values
                .Skip(1)
                .Aggregate(new HashSet<string>(perFramework.Values.First(), StringComparer.Ordinal), (set, next) =>
                {
                    set.IntersectWith(next);
                    return set;
                });
            var union = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in perFramework.Values)
                union.UnionWith(set);

            var unique = union.Count - shared.Count;
            if (unique == 0)
            {
                identicalPackages++;
                continue;
            }

            // A framework whose assembly declares nothing is a type-forwarding facade: the API
            // lives in the platform there. That is real divergence but a degenerate kind, and it
            // would answer the framework question with a case nobody queries.
            if (perFramework.Values.Any(set => set.Count == 0))
            {
                facades++;
                continue;
            }

            var detail = string.Join(
                ", ",
                perFramework.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => $"{item.Key}:{item.Value.Count}"));
            findings.Add((
                $"{packageId} {Path.GetFileName(versionDirectory)}",
                assemblyName,
                perFramework.Count,
                shared.Count,
                unique,
                detail));
        }
    }
}

Console.WriteLine($"multi-framework assemblies compared : {comparedPackages}");
Console.WriteLine($"  identical across every framework  : {identicalPackages}");
Console.WriteLine($"  type-forwarding facade (excluded) : {facades}");
Console.WriteLine($"  DIVERGING with real surface both sides : {findings.Count}");
Console.WriteLine();
Console.WriteLine("diverging, largest divergence first:");
foreach (var finding in findings.OrderByDescending(item => item.Unique).Take(40))
{
    Console.WriteLine($"  {finding.Unique,7} unique / {finding.Shared,7} shared   {finding.Package} :: {finding.Assembly}");
    Console.WriteLine($"          {finding.Detail}");
}

return 0;
