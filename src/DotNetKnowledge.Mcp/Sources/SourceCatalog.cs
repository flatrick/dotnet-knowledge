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
public sealed record SourceDefinition(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pin")] string Pin,
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("sparse")] IReadOnlyList<string> Sparse,
    [property: JsonPropertyName("purpose")] string Purpose);

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

    public SourceCatalog() => _sources = new Lazy<IReadOnlyDictionary<string, SourceDefinition>>(Load);

    public IReadOnlyDictionary<string, SourceDefinition> Sources => _sources.Value;

    public bool TryGet(string name, out SourceDefinition definition) =>
        Sources.TryGetValue(name, out definition!);

    private static Dictionary<string, SourceDefinition> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
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

        if (parsed.Sources is null || parsed.Sources.Count == 0)
            throw new InvalidDataException($"{path} declares no sources.");

        return parsed.Sources;
    }
}
