#!/usr/bin/env dotnet
#:property Nullable=enable
// The server disables reflection-based serialization for its own trimmed publish, and the setting
// reaches this script through the project reference; a probe is neither trimmed nor published.
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. It runs the shipped MetadataApiReader over every lib/<framework>/*.dll in a
// package folder tree -- the machine's NuGet cache by default -- and reports how many assemblies it
// can read, grouping the refusals by reason.
//
// The question it answers: is the set of metadata shapes the reader does not model small and nearly
// closed, or open-ended? That decides whether the shapes should keep being fixed one at a time or
// whether an undecodable declaration should stop costing the whole package.
// docs/backlog/undecodable-metadata-fails-the-whole-package.md carries the measurement.
//
//   dotnet run --file sweep-api-reader.cs
//   dotnet run --file sweep-api-reader.cs -- --root <packages-dir> --limit 200
//
// It reads whatever the machine happens to have restored, so the rate is a property of that sample
// and not of NuGet as a whole. It never downloads and never loads an assembly for execution.

using System.Diagnostics;
using System.Text;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

var root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var limit = int.MaxValue;
for (var index = 0; index < args.Length - 1; index++)
{
    if (string.Equals(args[index], "--root", StringComparison.Ordinal))
        root = args[index + 1];
    if (string.Equals(args[index], "--limit", StringComparison.Ordinal))
        limit = int.Parse(args[index + 1]);
}

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"No package folder at '{root}'.");
    return 2;
}

var readCount = 0;
var refusedCount = 0;
var otherCount = 0;
var byReason = new Dictionary<string, (int Count, string Example)>(StringComparer.Ordinal);
var readPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var refusedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var stopwatch = Stopwatch.StartNew();

foreach (var packageDirectory in Directory
    .EnumerateDirectories(root)
    .OrderBy(directory => directory, StringComparer.Ordinal))
{
    var packageId = Path.GetFileName(packageDirectory);
    foreach (var versionDirectory in Directory.EnumerateDirectories(packageDirectory))
    {
        var libraryRoot = Path.Combine(versionDirectory, "lib");
        if (!Directory.Exists(libraryRoot))
            continue;

        foreach (var frameworkDirectory in Directory.EnumerateDirectories(libraryRoot))
        {
            foreach (var assemblyPath in Directory.EnumerateFiles(frameworkDirectory, "*.dll"))
            {
                // Satellite resource assemblies carry no API. They are the only exclusion, so the
                // rate below is over everything else rather than over a curated subset.
                if (assemblyPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (readCount + refusedCount >= limit)
                    goto finished;

                try
                {
                    using var stream = File.OpenRead(assemblyPath);
                    MetadataApiReader.Read(stream);
                    readCount++;
                    readPackages.Add(packageId);
                }
                catch (InvalidDataException exception)
                {
                    refusedCount++;
                    refusedPackages.Add(packageId);
                    Record(
                        byReason,
                        CollapseQuoted(exception.Message),
                        $"{packageId} :: {Path.GetFileName(frameworkDirectory)} :: {Path.GetFileName(assemblyPath)}");
                }
                catch (Exception exception)
                {
                    // A native or malformed image is a different question from a shape the reader
                    // does not model, so it is counted apart rather than folded into the rate.
                    otherCount++;
                    Record(byReason, $"[not InvalidDataException] {exception.GetType().Name}", packageId);
                }
            }
        }
    }
}

finished:
var firstPartyRefused = refusedPackages.Where(IsFirstParty).OrderBy(id => id, StringComparer.Ordinal).ToArray();
var firstPartyTotal = readPackages.Concat(refusedPackages)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Count(IsFirstParty);

Console.WriteLine($"assemblies read      : {readCount}");
Console.WriteLine($"assemblies refused   : {refusedCount}");
Console.WriteLine($"other errors         : {otherCount}");
Console.WriteLine($"packages clean       : {readPackages.Except(refusedPackages, StringComparer.OrdinalIgnoreCase).Count()}");
Console.WriteLine($"packages with refusal: {refusedPackages.Count}");
// The server reads only packages an operator catalogs, which are realistically first-party, so this
// is the decision-relevant rate and the overall one is context.
Console.WriteLine($"first-party packages : {firstPartyTotal}, of which refused: {firstPartyRefused.Length}");
Console.WriteLine($"elapsed              : {stopwatch.Elapsed.TotalSeconds:F1}s");

if (firstPartyRefused.Length > 0)
{
    Console.WriteLine();
    Console.WriteLine("first-party packages with a refusal:");
    foreach (var id in firstPartyRefused)
        Console.WriteLine($"  {id}");
}

Console.WriteLine();
Console.WriteLine("refusal reasons, most common first:");
foreach (var pair in byReason.OrderByDescending(pair => pair.Value.Count))
{
    Console.WriteLine($"  {pair.Value.Count,5}  {pair.Key}");
    Console.WriteLine($"         e.g. {pair.Value.Example}");
}

return refusedCount == 0 ? 0 : 1;

static bool IsFirstParty(string packageId) =>
    packageId.StartsWith("microsoft.", StringComparison.OrdinalIgnoreCase)
    || packageId.StartsWith("system.", StringComparison.OrdinalIgnoreCase);

static void Record(Dictionary<string, (int Count, string Example)> byReason, string reason, string example)
{
    byReason.TryGetValue(reason, out var entry);
    byReason[reason] = (entry.Count + 1, entry.Example ?? example);
}

// The quoted part of a message names the declaration, which differs per assembly. Collapsing it is
// what makes one defect group as one reason instead of as hundreds.
static string CollapseQuoted(string message)
{
    var result = new StringBuilder(message.Length);
    var inQuote = false;
    foreach (var character in message)
    {
        if (character == '\'')
        {
            result.Append(inQuote ? ">'" : "'<");
            inQuote = !inQuote;
            continue;
        }

        if (!inQuote)
            result.Append(character);
    }

    return result.ToString();
}
