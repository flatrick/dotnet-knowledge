using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetKnowledge.CSharpScriptHost;

internal static class ScenarioDescriptorLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static ScenarioDescriptor Load(string path)
    {
        string descriptorPath;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            descriptorPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ScenarioDescriptorValidationException(
                path ?? "<null>",
                exception.Message,
                exception);
        }

        try
        {
            var descriptor = JsonSerializer.Deserialize<ScenarioDescriptor>(
                    File.ReadAllText(descriptorPath),
                    SerializerOptions)
                ?? throw new JsonException("Scenario descriptor must not be null.");
            var scenarioDirectory = Path.GetDirectoryName(descriptorPath)
                ?? throw new InvalidDataException("Could not determine the scenario directory.");
            var errors = descriptor.Validate(scenarioDirectory);
            if (errors.Count != 0)
            {
                throw new ScenarioDescriptorValidationException(
                    descriptorPath,
                    string.Join($"{Environment.NewLine}- ", errors));
            }

            return descriptor;
        }
        catch (ScenarioDescriptorValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new ScenarioDescriptorValidationException(
                descriptorPath,
                exception.Message,
                exception);
        }
    }
}
