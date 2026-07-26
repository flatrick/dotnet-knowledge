using System.Xml.Linq;

namespace DotNetKnowledge.Corpus.Tests.Projects;

internal sealed record CorpusProject(
    string RepositoryRelativePath,
    string TargetFramework,
    string LanguageVersion);

internal static class CorpusProjectDiscovery
{
    private static readonly string[] ExcludedDirectoryNames = ["bin", "obj", ".artifacts"];

    public static IReadOnlyList<CorpusProject> FindSdkStyleLibraries(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var corpusRoot = Path.Combine(
            fullRepositoryRoot,
            "examples",
            "language-features",
            "CSharp",
            "dotnet");
        if (!Directory.Exists(corpusRoot))
        {
            throw new DirectoryNotFoundException($"The SDK-style C# corpus directory does not exist: {corpusRoot}.");
        }

        return Directory.EnumerateFiles(corpusRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !HasExcludedDirectory(corpusRoot, path))
            .Select(ReadProject)
            .Where(project => project.IsSdkStyle && project.IsLibrary && !project.AllowsUnsafeBlocks)
            .Select(project => new CorpusProject(
                Path.GetRelativePath(fullRepositoryRoot, project.Path).Replace('\\', '/'),
                project.TargetFramework!,
                project.LanguageVersion!))
            .OrderBy(project => project.RepositoryRelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static DiscoveredProject ReadProject(string path)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            throw new InvalidOperationException($"Could not read corpus project XML: {path}.", exception);
        }

        var project = document.Root;
        if (project is null)
        {
            throw new InvalidOperationException($"Corpus project has no XML root element: {path}.");
        }

        var sdk = project.Attribute("Sdk")?.Value;
        var isSdkStyle =
            !string.IsNullOrWhiteSpace(sdk) ||
            project.Elements().Any(element =>
                element.Name.LocalName == "Sdk" &&
                !string.IsNullOrWhiteSpace(element.Attribute("Name")?.Value));
        if (!isSdkStyle)
        {
            return new DiscoveredProject(Path.GetFullPath(path), false, false, false, null, null);
        }

        var targetFramework = ReadOptionalSingleValue(project, "TargetFramework", path);
        var targetFrameworks = ReadOptionalSingleValue(project, "TargetFrameworks", path);
        if (targetFramework is null && targetFrameworks is null)
        {
            throw new InvalidOperationException(
                $"SDK-style corpus project is missing TargetFramework or TargetFrameworks: {path}.");
        }

        if (targetFramework is not null && targetFrameworks is not null)
        {
            throw new InvalidOperationException(
                $"SDK-style corpus project has contradictory TargetFramework and TargetFrameworks values: {path}.");
        }

        var languageVersion = ReadRequiredSingleValue(project, "LangVersion", path);
        var outputType = ReadOptionalSingleValue(project, "OutputType", path);
        var allowUnsafeBlocks = ReadOptionalSingleValue(project, "AllowUnsafeBlocks", path);

        var isLibrary =
            outputType is null ||
            string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase);
        var allowsUnsafe = allowUnsafeBlocks is not null && ParseBoolean(allowUnsafeBlocks, "AllowUnsafeBlocks", path);

        return new DiscoveredProject(
            Path.GetFullPath(path),
            true,
            isLibrary,
            allowsUnsafe,
            targetFramework ?? targetFrameworks!,
            languageVersion);
    }

    private static string ReadRequiredSingleValue(XElement project, string propertyName, string path) =>
        ReadOptionalSingleValue(project, propertyName, path)
        ?? throw new InvalidOperationException(
            $"SDK-style corpus project is missing {propertyName}: {path}.");

    private static string? ReadOptionalSingleValue(XElement project, string propertyName, string path)
    {
        var elements = project
            .Descendants()
            .Where(element => element.Name.LocalName == propertyName)
            .ToArray();
        if (elements.Length == 0)
        {
            return null;
        }

        if (elements.Any(element => string.IsNullOrWhiteSpace(element.Value)))
        {
            throw new InvalidOperationException(
                $"SDK-style corpus project has an empty {propertyName} value: {path}.");
        }

        var values = elements
            .Select(element => element.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length != 1)
        {
            throw new InvalidOperationException(
                $"SDK-style corpus project has contradictory {propertyName} values: {path}.");
        }

        return values[0];
    }

    private static bool ParseBoolean(string value, string propertyName, string path)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"SDK-style corpus project has an invalid {propertyName} value '{value}': {path}.");
    }

    private static bool HasExcludedDirectory(string corpusRoot, string path)
    {
        var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(corpusRoot, path));
        if (relativeDirectory is null)
        {
            return false;
        }

        return relativeDirectory
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => ExcludedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record DiscoveredProject(
        string Path,
        bool IsSdkStyle,
        bool IsLibrary,
        bool AllowsUnsafeBlocks,
        string? TargetFramework,
        string? LanguageVersion);
}
