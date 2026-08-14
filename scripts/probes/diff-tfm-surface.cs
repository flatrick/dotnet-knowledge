#!/usr/bin/env dotnet
#:property Nullable=enable
// The server disables reflection-based serialization for its own trimmed publish, and the setting
// reaches this script through the project reference; a probe is neither trimmed nor published.
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. It runs the SHIPPED public-API reader -- MetadataApiReader.Read -- over every
// lib/<framework>/<assembly>.dll of one package already on disk, and diffs the resulting public
// type and member sets pairwise.
//
// The question it answers: does a package's public API surface actually differ between its target
// frameworks? The server normalizes one corpus per framework and lets a caller select between them,
// which is only worth its cost if some package's surfaces diverge. A compiler XML file that differs
// per framework does not settle it, because the difference is routinely internal.
//
//   dotnet run --file diff-tfm-surface.cs -- --package <dir-containing-lib/> --assembly <name>
//
// It never downloads, and it reads only what the operator points it at. So it says nothing about
// any package it is not run against -- which is the whole of its limitation, because the finding it
// supports is a claim about packages in general drawn from the few measured so far.
// docs/backlog/framework-selection-has-no-observable-effect.md carries the measurements.

using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

string? packageDirectory = null;
string? assemblyName = null;
for (var index = 0; index < args.Length - 1; index++)
{
    if (string.Equals(args[index], "--package", StringComparison.Ordinal))
        packageDirectory = args[index + 1];
    if (string.Equals(args[index], "--assembly", StringComparison.Ordinal))
        assemblyName = args[index + 1];
}

if (packageDirectory is null || assemblyName is null)
{
    Console.Error.WriteLine(
        "usage: dotnet run --file diff-tfm-surface.cs -- --package <dir-containing-lib/> --assembly <name>");
    return 2;
}

var libraryRoot = Path.Combine(packageDirectory, "lib");
if (!Directory.Exists(libraryRoot))
{
    Console.Error.WriteLine($"No lib/ directory under '{packageDirectory}'.");
    return 2;
}

var surfaces = new Dictionary<string, (HashSet<string> Types, HashSet<string> Members)>(
    StringComparer.Ordinal);

foreach (var frameworkDirectory in Directory
    .EnumerateDirectories(libraryRoot)
    .OrderBy(directory => directory, StringComparer.Ordinal))
{
    var framework = Path.GetFileName(frameworkDirectory);
    var assemblyPath = Path.Combine(frameworkDirectory, assemblyName + ".dll");
    if (!File.Exists(assemblyPath))
        continue;

    await using var stream = File.OpenRead(assemblyPath);
    var corpus = MetadataApiReader.Read(stream);
    var types = corpus.Types
        .Select(type => type.FullName)
        .ToHashSet(StringComparer.Ordinal);
    var members = corpus.Types
        .SelectMany(type => type.Members.Select(member => $"{type.FullName}::{member.Signature}"))
        .ToHashSet(StringComparer.Ordinal);
    surfaces[framework] = (types, members);
    Console.WriteLine($"{framework,-16} publicTypes={types.Count,5}  publicMembers={members.Count,6}");
}

if (surfaces.Count == 0)
{
    Console.Error.WriteLine($"No lib/<framework>/{assemblyName}.dll found.");
    return 2;
}

Console.WriteLine();
var frameworks = surfaces.Keys.ToList();
var anyDifference = false;
for (var left = 0; left < frameworks.Count; left++)
{
    for (var right = left + 1; right < frameworks.Count; right++)
    {
        var (leftTypes, leftMembers) = surfaces[frameworks[left]];
        var (rightTypes, rightMembers) = surfaces[frameworks[right]];
        var typesOnlyLeft = leftTypes.Except(rightTypes).ToList();
        var typesOnlyRight = rightTypes.Except(leftTypes).ToList();
        var membersOnlyLeft = leftMembers.Except(rightMembers).ToList();
        var membersOnlyRight = rightMembers.Except(leftMembers).ToList();
        var identical = typesOnlyLeft.Count == 0
            && typesOnlyRight.Count == 0
            && membersOnlyLeft.Count == 0
            && membersOnlyRight.Count == 0;
        anyDifference |= !identical;

        Console.WriteLine(
            $"=== {frameworks[left]} vs {frameworks[right]}: {(identical ? "IDENTICAL" : "DIFFERENT")}");
        if (identical)
            continue;

        Print($"types only in {frameworks[left]}", typesOnlyLeft);
        Print($"types only in {frameworks[right]}", typesOnlyRight);
        Print($"members only in {frameworks[left]}", membersOnlyLeft);
        Print($"members only in {frameworks[right]}", membersOnlyRight);
        Console.WriteLine();
    }
}

// A difference is the interesting outcome, so it is the one that shows up in the exit code.
return anyDifference ? 1 : 0;

// Capped so one wildly divergent pair cannot bury the summary, and the count is always printed so
// the cap is visible rather than silent.
static void Print(string label, List<string> items)
{
    if (items.Count == 0)
        return;

    Console.WriteLine($"  {label} ({items.Count}):");
    foreach (var item in items.OrderBy(item => item, StringComparer.Ordinal).Take(12))
        Console.WriteLine($"    {item}");
    if (items.Count > 12)
        Console.WriteLine($"    ... and {items.Count - 12} more");
}
