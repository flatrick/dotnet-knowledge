using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

namespace DotNetKnowledge.Mcp.Sources;

public sealed class ApiPackageGenerationContributor(
    INuGetPackageClient packageClient,
    PackageApiCorpusBuilder corpusBuilder) : ISourceGenerationContributor
{
    private const string CorpusRootName = "api-packages";
    private const string DownloadRootName = ".package-downloads";
    private const string RoslynApiDocsRepository = "dotnet/roslyn-api-docs";

    public bool AppliesTo(SourceDefinition definition) => AppliesToSource(definition);

    internal static bool AppliesToSource(SourceDefinition definition) =>
        string.Equals(definition.Repository, RoslynApiDocsRepository, StringComparison.Ordinal)
        && definition.ApiPackages is { Count: > 0 };

    public async Task<IReadOnlyList<ApiPackageSyncState>> BuildAsync(
        SourceDefinition definition,
        string refLabel,
        string repositoryDirectory,
        string supplementsDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(refLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplementsDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var packages = definition.ApiPackages ?? [];
        if (packages.Count == 0)
            return [];

        var pinned = string.Equals(refLabel, "pinned", StringComparison.Ordinal);
        if (!pinned && !refLabel.StartsWith("head:", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported synchronized source ref '{refLabel}'.");

        var cohortVersion = RoslynPackageCohort.Read(repositoryDirectory, packages, pinned);
        var supplementsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(supplementsDirectory));
        Directory.CreateDirectory(supplementsRoot);
        var downloadRoot = GetContainedPath(supplementsRoot, DownloadRootName);
        var corpusRoot = GetContainedPath(supplementsRoot, CorpusRootName);
        var staged = new List<StagedPackage>(packages.Count);

        try
        {
            progress?.Report("package-download");
            Directory.CreateDirectory(downloadRoot);
            for (var index = 0; index < packages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var package = packages[index];
                var archivePath = GetContainedPath(
                    downloadRoot,
                    $"{index:D4}-{package.PackageId}.nupkg");
                var download = await packageClient.DownloadAsync(
                    package,
                    cohortVersion,
                    pinned ? package.Sha512 : null,
                    archivePath,
                    cancellationToken).ConfigureAwait(false);
                var synchronizedPackage = package.WithSynchronizedContent(cohortVersion, download.Sha512);
                staged.Add(new StagedPackage(package, synchronizedPackage, download, archivePath));
            }

            progress?.Report("package-validate");
            foreach (var package in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assets = PackageArchiveReader.ReadAssets(package.ArchivePath, package.SynchronizedDefinition);
                if (!assets.Any(asset => string.Equals(
                    asset.Framework,
                    package.CatalogDefinition.DefaultFramework,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        $"Package '{package.CatalogDefinition.PackageId}' does not contain its default framework " +
                        $"'{package.CatalogDefinition.DefaultFramework}'.");
                }
            }

            progress?.Report("package-normalize");
            var states = new List<ApiPackageSyncState>(staged.Count);
            foreach (var package in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var corpusDirectory = GetContainedPath(corpusRoot, package.CatalogDefinition.PackageId);
                var result = await corpusBuilder.BuildAsync(
                    package.ArchivePath,
                    package.SynchronizedDefinition,
                    corpusDirectory,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.AvailableFrameworks.Any(framework => string.Equals(
                    framework,
                    package.CatalogDefinition.DefaultFramework,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        $"Package '{package.CatalogDefinition.PackageId}' did not build its default framework " +
                        $"'{package.CatalogDefinition.DefaultFramework}'.");
                }

                var relativeCorpusDirectory = Path.GetRelativePath(supplementsRoot, corpusDirectory);
                EnsureRelativePath(relativeCorpusDirectory);
                states.Add(new ApiPackageSyncState(
                    package.CatalogDefinition.PackageId,
                    package.CatalogDefinition.AssemblyName,
                    cohortVersion,
                    package.Download.Sha512,
                    package.CatalogDefinition.Feed,
                    package.Download.FetchedAt,
                    package.CatalogDefinition.DefaultFramework,
                    result.AvailableFrameworks,
                    relativeCorpusDirectory,
                    PackageApiCorpusStore.SchemaVersion));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return states;
        }
        finally
        {
            if (Directory.Exists(downloadRoot))
                Directory.Delete(downloadRoot, recursive: true);
        }
    }

    private static string GetContainedPath(string root, params string[] segments)
    {
        var candidate = Path.GetFullPath(Path.Combine([root, .. segments]));
        var relative = Path.GetRelativePath(root, candidate);
        EnsureRelativePath(relative);
        return candidate;
    }

    private static void EnsureRelativePath(string relative)
    {
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Package synchronization path escapes the supplements directory.");
        }
    }

    private sealed record StagedPackage(
        ApiPackageDefinition CatalogDefinition,
        ApiPackageDefinition SynchronizedDefinition,
        NuGetPackageDownload Download,
        string ArchivePath);
}
