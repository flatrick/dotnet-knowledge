using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public sealed record PackageCorpusBuildResult(
    IReadOnlyList<string> AvailableFrameworks,
    IReadOnlyDictionary<string, string> CorpusFiles);

public sealed class PackageApiCorpusBuilder
{
    private readonly Action<string>? afterFrameworkStaged;
    private readonly Action? afterOldOutputMoved;

    public PackageApiCorpusBuilder()
    {
    }

    internal PackageApiCorpusBuilder(Action<string> afterFrameworkStaged) =>
        this.afterFrameworkStaged = afterFrameworkStaged;

    internal PackageApiCorpusBuilder(Action afterOldOutputMoved) =>
        this.afterOldOutputMoved = afterOldOutputMoved;

    public async Task<PackageCorpusBuildResult> BuildAsync(
        string packagePath,
        ApiPackageDefinition definition,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var assets = PackageArchiveReader.ReadAssets(packagePath, definition);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var parent = Path.GetDirectoryName(output)
            ?? throw new ArgumentException("outputDirectory must have a parent directory.", nameof(outputDirectory));
        if (Path.GetFileName(output).Length == 0)
            throw new ArgumentException("outputDirectory must name a directory below its root.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var asset in assets.OrderBy(item => item.Framework, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assembly = archive.GetEntry(asset.AssemblyEntry)
                    ?? throw new InvalidDataException($"Package assembly entry '{asset.AssemblyEntry}' is missing.");
                var xml = archive.GetEntry(asset.XmlEntry)
                    ?? throw new InvalidDataException($"Package XML entry '{asset.XmlEntry}' is missing.");
                var assemblyBytes = await ReadEntryAsync(assembly, cancellationToken).ConfigureAwait(false);
                using var assemblyStream = new MemoryStream(assemblyBytes, writable: false);
                ValidateAssemblyName(assemblyStream, definition.AssemblyName);
                assemblyStream.Position = 0;
                var metadata = MetadataApiReader.Read(assemblyStream);
                using var xmlStream = xml.Open();
                var docs = PackageXmlDocsReader.Read(xmlStream);
                var corpus = Join(metadata, docs);
                await PackageApiCorpusStore.WriteAsync(
                    Path.Combine(staging, asset.Framework + ".json"), definition, asset.Framework, corpus, cancellationToken)
                    .ConfigureAwait(false);
                files.Add(asset.Framework, Path.Combine(output, asset.Framework + ".json"));
                afterFrameworkStaged?.Invoke(asset.Framework);
            }

            cancellationToken.ThrowIfCancellationRequested();
            PublishDirectory(staging, output, afterOldOutputMoved);
            staging = string.Empty;
        }
        finally
        {
            if (staging.Length > 0 && Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }

        return new PackageCorpusBuildResult(files.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray(), files);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        await using var target = new MemoryStream();
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return target.ToArray();
    }

    private static void ValidateAssemblyName(Stream stream, string expectedAssemblyName)
    {
        try
        {
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!reader.HasMetadata)
                throw new InvalidDataException("The package assembly has no managed metadata.");
            var assembly = reader.GetMetadataReader().GetAssemblyDefinition();
            var name = reader.GetMetadataReader().GetString(assembly.Name);
            if (!string.Equals(name, expectedAssemblyName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The package assembly name '{name}' does not match '{expectedAssemblyName}'.");
            }
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The package assembly is not valid managed metadata.", exception);
        }
    }

    private static ApiCorpus Join(ApiCorpus metadata, IReadOnlyDictionary<string, ApiDocumentation> docs) =>
        NormalizeForStorage(metadata with
        {
            Types = metadata.Types.Select(type => type with
            {
                Documentation = docs.TryGetValue(type.EcmaId, out var typeDocs)
                    ? typeDocs
                    : EmptyDocumentation(),
                Members = type.Members.Select(member => member with
                {
                    Documentation = docs.TryGetValue(member.EcmaId, out var memberDocs)
                        ? memberDocs
                        : EmptyDocumentation(),
                }).ToArray(),
            }).ToArray(),
        });

    internal static ApiCorpus NormalizeForStorage(ApiCorpus corpus) => corpus with
    {
        Types = corpus.Types.Select(type => type with
        {
            Documentation = NormalizeDocumentation(type.Documentation),
            Members = type.Members.Select(member => member with
            {
                Documentation = NormalizeDocumentation(member.Documentation),
                Parameters = member.Parameters.Select(NormalizeTypeUse).ToArray(),
                ReturnType = member.ReturnType is null ? null : NormalizeTypeUse(member.ReturnType),
                Constraints = member.Constraints.Select(NormalizeTypeUse)
                    .OrderBy(item => item.TypeExpression, StringComparer.Ordinal).ToArray(),
                Attributes = NormalizeAttributes(member.Attributes),
            }).OrderBy(member => member.EcmaId, StringComparer.Ordinal).ToArray(),
            BaseType = type.BaseType is null ? null : NormalizeTypeUse(type.BaseType),
            Interfaces = type.Interfaces.Select(NormalizeTypeUse)
                .OrderBy(item => item.TypeExpression, StringComparer.Ordinal).ToArray(),
            Constraints = type.Constraints.Select(NormalizeTypeUse)
                .OrderBy(item => item.TypeExpression, StringComparer.Ordinal).ToArray(),
            Attributes = NormalizeAttributes(type.Attributes),
        }).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray(),
    };

    private static ApiTypeUse NormalizeTypeUse(ApiTypeUse item) => item with
    {
        TypeNames = item.TypeNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
    };

    private static ApiAttributeUse[] NormalizeAttributes(IReadOnlyList<ApiAttributeUse> attributes) => attributes
        .Select(attribute => attribute with
        {
            ArgumentTypeNames = attribute.ArgumentTypeNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
        })
        .OrderBy(attribute => attribute.Application, StringComparer.Ordinal)
        .ThenBy(attribute => attribute.AttributeType, StringComparer.Ordinal)
        .ToArray();

    private static ApiDocumentation NormalizeDocumentation(ApiDocumentation documentation) => documentation with
    {
        Parameters = NormalizeNamedDocumentation(documentation.Parameters),
        TypeParameters = NormalizeNamedDocumentation(documentation.TypeParameters),
        Exceptions = NormalizeNamedDocumentation(documentation.Exceptions),
    };

    private static ApiNamedDocumentation[] NormalizeNamedDocumentation(
        IReadOnlyList<ApiNamedDocumentation> items) => items
        .OrderBy(item => item.Name, StringComparer.Ordinal)
        .ThenBy(item => item.Text, StringComparer.Ordinal)
        .ToArray();

    private static ApiDocumentation EmptyDocumentation() => new(
        null, Array.Empty<ApiNamedDocumentation>(), Array.Empty<ApiNamedDocumentation>(), null, null, null,
        Array.Empty<ApiNamedDocumentation>());

    private static void PublishDirectory(string staging, string output, Action? afterOldOutputMoved)
    {
        var backup = Path.Combine(
            Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.backup");
        var movedExisting = false;
        var conflict = Path.Combine(
            Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.conflict");
        try
        {
            if (Directory.Exists(output))
            {
                Directory.Move(output, backup);
                movedExisting = true;
                afterOldOutputMoved?.Invoke();
            }

            Directory.Move(staging, output);
        }
        catch
        {
            if (movedExisting)
                RestorePriorDirectory(backup, output, conflict);
            throw;
        }

        if (movedExisting)
            TryDeleteRetiredDirectory(backup);
    }

    private static void RestorePriorDirectory(
        string backup,
        string output,
        string conflict)
    {
        if (Directory.Exists(output))
            Directory.Move(output, conflict);
        else if (File.Exists(output))
            File.Move(output, conflict);
        Directory.Move(backup, output);
    }

    private static void TryDeleteRetiredDirectory(string backup)
    {
        try
        {
            Directory.Delete(backup, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A completed output is already published. Retaining its retired predecessor is safer
            // than changing that success into a failure because a cleanup handle is still open.
        }
    }
}
