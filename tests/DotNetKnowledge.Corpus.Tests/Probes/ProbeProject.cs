using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetKnowledge.Corpus.Tests.Cases;
using DotNetKnowledge.Corpus.Tests.Execution;
using DotNetKnowledge.Corpus.Tests.Toolchains;

namespace DotNetKnowledge.Corpus.Tests.Probes;

internal sealed partial class ProbeProject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string OwnedRoot = Path.Combine(
        Path.GetTempPath(),
        "dotnet-knowledge-corpus-tests");

    public static async Task<ProbeResult> BuildAsync(
        InstalledSdk sdk,
        CompilationExpectation expectation,
        string sourcePath,
        string? harnessPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var resolvedSourcePath = ResolveRepositoryPath(sourcePath);
        var resolvedHarnessPath = harnessPath is null ? null : ResolveRepositoryPath(harnessPath);
        EnsureSourceExists(resolvedSourcePath, nameof(sourcePath));
        if (resolvedHarnessPath is not null)
        {
            EnsureSourceExists(resolvedHarnessPath, nameof(harnessPath));
        }

        var resolvedOwnedRoot = Path.GetFullPath(OwnedRoot);
        Directory.CreateDirectory(resolvedOwnedRoot);
        var projectDirectory = Path.Combine(resolvedOwnedRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        ProbeResult? result = null;
        try
        {
            var globalJsonPath = Path.Combine(projectDirectory, "global.json");
            var projectPath = Path.Combine(projectDirectory, "probe.csproj");
            await WriteGlobalJson(globalJsonPath, sdk, cancellationToken);
            await WriteProject(
                projectPath,
                expectation,
                resolvedSourcePath,
                resolvedHarnessPath,
                cancellationToken);

            var runner = new ProcessRunner();
            var process = await runner.RunAsync(
                DotNetHost(sdk),
                ["build", "probe.csproj", "-t:Rebuild", "--nologo", "-v:minimal"],
                projectDirectory,
                new Dictionary<string, string?> { ["MSBuildSDKsPath"] = null },
                cancellationToken);
            result = new ProbeResult(
                process,
                ExtractDiagnostics(process),
                resolvedOwnedRoot,
                projectDirectory,
                globalJsonPath,
                projectPath);
            return result;
        }
        finally
        {
            if (result is null)
            {
                ProbeResult.DeleteOwnedDirectory(resolvedOwnedRoot, projectDirectory);
            }
        }
    }

    private static async Task WriteGlobalJson(
        string path,
        InstalledSdk sdk,
        CancellationToken cancellationToken)
    {
        var document = new
        {
            sdk = new
            {
                version = sdk.Version.ToString(3),
                rollForward = "disable"
            }
        };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static async Task WriteProject(
        string path,
        CompilationExpectation expectation,
        string sourcePath,
        string? harnessPath,
        CancellationToken cancellationToken)
    {
        var propertyGroup = new XElement(
            "PropertyGroup",
            new XElement("TargetFramework", expectation.TargetFramework),
            new XElement("LangVersion", expectation.LanguageVersion),
            new XElement("TreatWarningsAsErrors", "true"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("GenerateTargetFrameworkAttribute", "false"));
        if (harnessPath is not null)
        {
            propertyGroup.Add(new XElement("OutputType", "Exe"));
        }

        var itemGroup = new XElement(
            "ItemGroup",
            CompileItem(sourcePath, "Subject.cs"));
        if (harnessPath is not null)
        {
            itemGroup.Add(CompileItem(harnessPath, "Program.cs"));
        }

        var document = new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                propertyGroup,
                itemGroup));
        await File.WriteAllTextAsync(path, document.ToString(), cancellationToken);
    }

    private static XElement CompileItem(string path, string link) =>
        new(
            "Compile",
            new XAttribute("Include", path),
            new XAttribute("Link", link));

    private static List<string> ExtractDiagnostics(ProcessResult process)
    {
        var diagnostics = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = process.StandardOutput + Environment.NewLine + process.StandardError;
        foreach (Match match in CompilerDiagnostic().Matches(output))
        {
            var code = match.Groups["code"].Value;
            if (seen.Add(code))
            {
                diagnostics.Add(code);
            }
        }

        return diagnostics;
    }

    private static string DotNetHost(InstalledSdk sdk)
    {
        var sdkRoot = Path.GetFullPath(sdk.Directory);
        var dotnetRoot = Directory.GetParent(Path.TrimEndingDirectorySeparator(sdkRoot))?.FullName
            ?? throw new InvalidOperationException($"Could not resolve the dotnet host from SDK directory {sdk.Directory}.");
        return Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    }

    private static string ResolveRepositoryPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(path, FindRepositoryRoot());
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "sources.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void EnsureSourceExists(string path, string parameterName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Probe source does not exist: {path}", parameterName);
        }
    }

    [GeneratedRegex("(?:^|[\\s:])(?<code>(?:CS|BC)\\d{4})(?=[:\\s])")]
    private static partial Regex CompilerDiagnostic();
}
