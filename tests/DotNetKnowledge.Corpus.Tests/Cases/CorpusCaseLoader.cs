using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.Corpus.Tests.Cases;

internal static class CorpusCaseLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static CorpusCase Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return JsonSerializer.Deserialize<CorpusCase>(File.ReadAllText(path), SerializerOptions)
            ?? throw new JsonException("Case document must not be null.");
    }
}
