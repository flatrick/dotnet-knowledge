using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.CSharpScriptHost;

internal static class ScenarioDescriptorLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ScenarioDescriptor Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var descriptor = JsonSerializer.Deserialize<ScenarioDescriptor>(File.ReadAllText(path), SerializerOptions)
            ?? throw new JsonException("Scenario descriptor must not be null.");
        var scenarioDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException($"Could not determine the scenario directory for {path}.");
        var errors = descriptor.Validate(scenarioDirectory);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                $"Scenario descriptor validation failed: {path}{Environment.NewLine}- " +
                string.Join($"{Environment.NewLine}- ", errors));
        }

        return descriptor;
    }
}
