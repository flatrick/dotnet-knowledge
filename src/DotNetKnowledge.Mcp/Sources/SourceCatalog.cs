using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// One upstream repository this server can query, as declared in <c>sources.json</c>.
/// </summary>
/// <param name="Url">Clone URL.</param>
/// <param name="Pin">The commit this repository vouches for.</param>
/// <param name="Head">The branch a caller reaches by asking for <c>head</c>, opting into drift.</param>
/// <param name="Sparse">Paths to sparse-checkout; the rest of the tree is never fetched.</param>
/// <param name="Purpose">One line on what the source answers, surfaced by <c>list_sources</c>.</param>
/// <param name="ApiPackages">Optional pinned NuGet packages that supplement this source's API docs.</param>
/// <param name="Markdown">
/// Whether <c>search_docs</c>/<c>get_doc</c>/<c>get_doc_outline</c> can
/// search and fetch this source's content. Defaults to <c>false</c> so an XML-docs source such as
/// <c>dotnet-api-docs</c> needs no entry to stay excluded.
/// </param>
public sealed record SourceDefinition(
    [property: JsonPropertyName("repository")] string Repository,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pin")] string Pin,
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("sparse")] IReadOnlyList<string> Sparse,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("apiPackages")] IReadOnlyList<ApiPackageDefinition>? ApiPackages = null,
    [property: JsonPropertyName("markdown")] bool Markdown = false);

internal sealed record SourcesFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("sources")] Dictionary<string, SourceDefinition> Sources);

/// <summary>
/// Reads <c>sources.json</c> from beside the server assembly.
/// </summary>
/// <remarks>
/// The file is loaded once and held: it declares which revisions this build vouches for, so a
/// mid-session change would silently split one session's answers across two sets of pins.
/// </remarks>
public sealed class SourceCatalog
{
    private const string FileName = "sources.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Lazy<IReadOnlyDictionary<string, SourceDefinition>> _sources;

    public SourceCatalog()
        : this(Path.Combine(AppContext.BaseDirectory, FileName))
    {
    }

    public SourceCatalog(string path) =>
        _sources = new Lazy<IReadOnlyDictionary<string, SourceDefinition>>(() => Load(path));

    public IReadOnlyDictionary<string, SourceDefinition> Sources => _sources.Value;

    public bool TryGet(string name, out SourceDefinition definition) =>
        Sources.TryGetValue(name, out definition!);

    private static Dictionary<string, SourceDefinition> Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{FileName} was not found next to the server assembly ({AppContext.BaseDirectory}). " +
                "It declares the upstream sources and their pinned commits; the server cannot answer " +
                "anything about fetched sources without it.",
                path);
        }

        var parsed = JsonSerializer.Deserialize<SourcesFile>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidDataException($"{path} did not parse into a sources document.");

        if (parsed.SchemaVersion != 1)
            throw new InvalidDataException($"{path} has unsupported schemaVersion {parsed.SchemaVersion}.");

        if (parsed.Sources is null || parsed.Sources.Count == 0)
            throw new InvalidDataException($"{path} declares no sources.");

        foreach (var (name, definition) in parsed.Sources)
        {
            ValidateSource(path, name, definition);
            parsed.Sources[name] = definition with { ApiPackages = definition.ApiPackages ?? [] };
        }

        return parsed.Sources;
    }

    private static void ValidateSource(string path, string name, SourceDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException($"{path} declares invalid source name '{name}'.");
        }

        if (definition is null
            || string.IsNullOrWhiteSpace(definition.Repository)
            || string.IsNullOrWhiteSpace(definition.Url)
            || string.IsNullOrWhiteSpace(definition.Head)
            || definition.Head.StartsWith('-')
            || string.IsNullOrWhiteSpace(definition.Purpose)
            || definition.Pin is not { Length: 40 }
            || definition.Pin.Any(character => !Uri.IsHexDigit(character))
            || definition.Sparse is not { Count: > 0 })
        {
            throw new InvalidDataException($"{path} declares incomplete source '{name}'.");
        }

        foreach (var sparsePath in definition.Sparse)
        {
            var segments = sparsePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (string.IsNullOrWhiteSpace(sparsePath)
                || Path.IsPathRooted(sparsePath)
                || sparsePath.Contains(':')
                || sparsePath.StartsWith('-')
                || segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"{path} source '{name}' declares invalid sparse path '{sparsePath}'.");
            }
        }

        ValidateApiPackages(path, name, definition.ApiPackages);
    }

    private static void ValidateApiPackages(
        string path,
        string sourceName,
        IReadOnlyList<ApiPackageDefinition>? packages)
    {
        if (packages is null)
            return;

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            if (package is null
                || !NuGetPackageIdentity.IsValidPackageId(package.PackageId)
                || !packageIds.Add(package.PackageId)
                || string.IsNullOrWhiteSpace(package.AssemblyName)
                || package.AssemblyName.Contains('/')
                || package.AssemblyName.Contains('\\')
                || package.AssemblyName.Contains(':')
                || !Uri.TryCreate(package.Feed, UriKind.Absolute, out var feed)
                || !string.Equals(feed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !IsNumericVersion(package.Version)
                || !IsSha512(package.Sha512)
                || string.IsNullOrWhiteSpace(package.DefaultFramework))
            {
                throw new InvalidDataException(
                    $"{path} source '{sourceName}' declares an invalid API package.");
            }
        }
    }

    private static bool IsNumericVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version)
        && version.All(character => char.IsAsciiDigit(character) || character == '.')
        && Version.TryParse(version, out _);

    private static bool IsSha512(string? sha512)
    {
        if (string.IsNullOrWhiteSpace(sha512))
            return false;

        try
        {
            return Convert.FromBase64String(sha512).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
