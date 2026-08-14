#!/usr/bin/env dotnet
#:property Nullable=enable
// The server disables reflection-based serialization for its own trimmed publish, and the setting
// reaches this script through the project reference; a probe is neither trimmed nor published.
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Runs the SHIPPED archive validator and corpus builder — PackageArchiveReader.ReadAssets,
// PackageApiCorpusBuilder.BuildAsync, PackageApiCorpusStore.Read — over one real .nupkg on disk,
// and prints what they made of it.
//
// The question it answers: does the pinned Microsoft.CodeAnalysis.Workspaces.MSBuild package have
// the layout, assembly names and documentation IDs the server assumes, and does MSBuildWorkspace
// come out of it with its four Create overloads intact? The automated suite proves the same code
// against repository-authored fixtures, which cannot answer that, because a fixture is built to the
// assumption rather than against it.
//
//   dotnet run --file probe-api-package-supplement.cs -- --package <path-to-.nupkg>
//
// It never downloads: the path is a copy the operator already has. So it says nothing about the
// download boundary, nothing about any other package, and nothing about layouts a future version
// might ship — one local file, at one version, on one machine.

using System.Security.Cryptography;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

var packagePath = ReadPackageArgument(args);
if (packagePath is null)
{
    Console.Error.WriteLine("usage: dotnet run --file probe-api-package-supplement.cs -- --package <path-to-.nupkg>");
    return 1;
}

if (!File.Exists(packagePath))
{
    Console.Error.WriteLine($"No package at '{packagePath}'.");
    return 1;
}

var catalog = LoadCatalog();
var definition = catalog.Sources["roslyn-api-docs"].ApiPackages?.FirstOrDefault();
if (definition is null)
{
    Console.Error.WriteLine("sources.json declares no apiPackages for roslyn-api-docs.");
    return 1;
}

var output = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-probe-corpus-{Guid.NewGuid():N}");
try
{
    string observedHash;
    await using (var stream = File.OpenRead(packagePath))
    {
        observedHash = Convert.ToBase64String(await SHA512.HashDataAsync(stream));
    }

    var assets = PackageArchiveReader.ReadAssets(packagePath, definition);
    var built = await new PackageApiCorpusBuilder()
        .BuildAsync(packagePath, definition, output, CancellationToken.None);

    var framework = definition.DefaultFramework;
    var corpus = PackageApiCorpusStore.Read(built.CorpusFiles[framework], definition, framework);
    var workspace = corpus.Types.SingleOrDefault(type =>
        string.Equals(type.Name, "MSBuildWorkspace", StringComparison.Ordinal));

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            package = definition.PackageId,
            version = definition.Version,
            observedSha512 = observedHash,
            // The catalog hash is what makes the answer pinned rather than merely local, so a
            // mismatch is the whole result and not a footnote to it.
            matchesCatalogSha512 = CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(observedHash), Convert.FromBase64String(definition.Sha512)),
            assetFrameworks = assets.Select(asset => asset.Framework).ToArray(),
            builtFrameworks = built.AvailableFrameworks,
            defaultFramework = framework,
            msBuildWorkspace = workspace is null
                ? null
                : new
                {
                    fullName = workspace.FullName,
                    memberCount = workspace.Members.Count,
                    createOverloads = workspace.Members
                        .Where(member => string.Equals(member.Name, "Create", StringComparison.Ordinal))
                        .Select(member => member.Signature)
                        .ToArray(),
                },
        },
        new JsonSerializerOptions { WriteIndented = true }));

    return workspace is null ? 1 : 0;
}
finally
{
    if (Directory.Exists(output))
        Directory.Delete(output, recursive: true);
}

static string? ReadPackageArgument(string[] arguments)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (string.Equals(arguments[index], "--package", StringComparison.Ordinal))
            return arguments[index + 1];
    }

    return null;
}

// Beside the assembly first, the way the server itself reads it; walking up from the working
// directory is the fallback for a build layout that did not copy it.
static SourceCatalog LoadCatalog()
{
    var beside = Path.Combine(AppContext.BaseDirectory, "sources.json");
    if (File.Exists(beside))
        return new SourceCatalog();

    for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
        directory is not null;
        directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "sources.json");
        if (File.Exists(candidate))
            return new SourceCatalog(candidate);
    }

    throw new FileNotFoundException("No sources.json beside the probe or above the working directory.");
}
