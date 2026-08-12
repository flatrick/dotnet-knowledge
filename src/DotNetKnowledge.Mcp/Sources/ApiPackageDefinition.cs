using System.Text.Json.Serialization;

namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// A pinned NuGet package that supplements an upstream source with API documentation assemblies.
/// </summary>
public sealed record ApiPackageDefinition(
    [property: JsonPropertyName("packageId")] string PackageId,
    [property: JsonPropertyName("assemblyName")] string AssemblyName,
    [property: JsonPropertyName("feed")] string Feed,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sha512")] string Sha512,
    [property: JsonPropertyName("defaultFramework")] string DefaultFramework);
