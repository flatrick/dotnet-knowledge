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

    public static IReadOnlyList<LoadedCorpusCase> LoadValidated(
        IEnumerable<string> paths,
        string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var loadedCases = paths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new LoadedCorpusCase(path, Load(path)))
            .ToArray();
        var errors = loadedCases
            .SelectMany(document => document.Case
                .Validate(repositoryRoot)
                .Select(error => $"{document.Path}: {error}"))
            .ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(
                $"Corpus case validation failed:{Environment.NewLine}- " +
                string.Join($"{Environment.NewLine}- ", errors));
        }

        return loadedCases;
    }
}

internal sealed record LoadedCorpusCase(string Path, CorpusCase Case);
