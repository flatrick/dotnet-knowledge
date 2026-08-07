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
        IReadOnlyList<string> projectReferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(expectation);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(projectReferences);

        var resolvedSourcePath = ResolveRepositoryPath(sourcePath);
        var resolvedHarnessPath = harnessPath is null ? null : ResolveRepositoryPath(harnessPath);
        EnsureSourceExists(resolvedSourcePath, nameof(sourcePath));
        if (resolvedHarnessPath is not null)
        {
            EnsureSourceExists(resolvedHarnessPath, nameof(harnessPath));
            EnsureSameLanguage(resolvedSourcePath, resolvedHarnessPath);
        }

        var resolvedProjectReferences = projectReferences.Select(ResolveRepositoryPath).ToArray();
        foreach (var resolvedProjectReference in resolvedProjectReferences)
        {
            EnsureSourceExists(resolvedProjectReference, nameof(projectReferences));
        }

        var resolvedOwnedRoot = Path.GetFullPath(OwnedRoot);
        Directory.CreateDirectory(resolvedOwnedRoot);
        var projectDirectory = Path.Combine(resolvedOwnedRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        ProbeResult? result = null;
        try
        {
            var globalJsonPath = Path.Combine(projectDirectory, "global.json");
            var projectFileName = $"probe.{ProjectExtension(resolvedSourcePath)}";
            var projectPath = Path.Combine(projectDirectory, projectFileName);
            await WriteGlobalJson(globalJsonPath, sdk, cancellationToken);
            await WriteProject(
                projectPath,
                expectation,
                resolvedSourcePath,
                resolvedHarnessPath,
                resolvedProjectReferences,
                cancellationToken);

            var runner = new ProcessRunner();
            var process = await runner.RunAsync(
                DotNetHost(sdk),
                ["build", projectFileName, "-t:Rebuild", "--nologo", "-v:minimal"],
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

    public static Task<ProcessResult> RunAsync(
        InstalledSdk sdk,
        ProbeResult successfulBuild,
        string targetFramework,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(successfulBuild);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);

        if (successfulBuild.Process.ExitCode != 0)
        {
            throw new InvalidOperationException("Cannot run a probe whose build failed.");
        }

        var outputRoot = Path.Combine(successfulBuild.ProjectDirectory, "bin");

        // A .NET Framework OutputType=Exe build has no dotnet host to launch and no separate
        // probe.dll -- the .exe it emits is the only assembly, and Windows launches it directly.
        if (NetFrameworkTargetFramework.IsFramework(targetFramework))
        {
            var frameworkProbePath = Directory
                .GetFiles(outputRoot, "probe.exe", SearchOption.AllDirectories)
                .Single();
            var frameworkOutputDirectory = Path.GetDirectoryName(frameworkProbePath)
                ?? throw new InvalidOperationException(
                    $"Could not resolve the probe output directory: {frameworkProbePath}.");

            return new ProcessRunner().RunAsync(
                Path.GetFullPath(frameworkProbePath),
                [],
                frameworkOutputDirectory,
                cancellationToken: cancellationToken);
        }

        var probePath = Directory
            .GetFiles(outputRoot, "probe.dll", SearchOption.AllDirectories)
            .Single();
        var outputDirectory = Path.GetDirectoryName(probePath)
            ?? throw new InvalidOperationException($"Could not resolve the probe output directory: {probePath}.");

        return new ProcessRunner().RunAsync(
            DotNetHost(sdk),
            [Path.GetFullPath(probePath)],
            outputDirectory,
            cancellationToken: cancellationToken);
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
        IReadOnlyList<string> projectReferences,
        CancellationToken cancellationToken)
    {
        var isVisualBasic = Path.GetExtension(sourcePath) == ".vb";
        var subjectExtension = isVisualBasic ? "vb" : "cs";

        var propertyGroup = new XElement(
            "PropertyGroup",
            new XElement("TargetFramework", expectation.TargetFramework),
            new XElement("LangVersion", expectation.LanguageVersion),
            new XElement("TreatWarningsAsErrors", "true"),
            // A probe is written under the temp directory, so no Directory.Build.props reaches it
            // and both halves of the zero-warning gate have to be stated here. Without the second,
            // an MSB#### warning leaves the build at exit 0, and the only thing that catches it is
            // CorpusCompilationTests matching on the severity word — which is localized.
            new XElement("MSBuildTreatWarningsAsErrors", "true"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("GenerateTargetFrameworkAttribute", "false"));
        if (harnessPath is not null)
        {
            propertyGroup.Add(
                new XElement("OutputType", "Exe"),
                new XElement("CheckEolTargetFramework", "false"));
        }

        if (isVisualBasic)
        {
            // VB prepends RootNamespace to every declaration at compile time, unlike C# where it is
            // inert for an explicit namespace block. Left unset it defaults to this project's own
            // name ("probe") and silently double-prefixes the subject's own Namespace statement.
            propertyGroup.Add(new XElement("RootNamespace", string.Empty));
        }

        var itemGroup = new XElement(
            "ItemGroup",
            CompileItem(sourcePath, $"Subject.{subjectExtension}"));
        if (harnessPath is not null)
        {
            itemGroup.Add(CompileItem(harnessPath, $"Program.{subjectExtension}"));
        }

        var document = new XDocument(
            new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                propertyGroup,
                itemGroup,
                ReferenceItemGroups(expectation.TargetFramework, projectReferences)));
        await File.WriteAllTextAsync(path, document.ToString(), cancellationToken);
    }

    private static IEnumerable<XElement> ReferenceItemGroups(
        string targetFramework,
        IReadOnlyList<string> projectReferences)
    {
        if (NetFrameworkTargetFramework.IsFramework(targetFramework))
        {
            // Supplies the net48 reference assemblies so the probe builds without depending on a
            // machine-installed .NET Framework targeting pack -- the same package the corpus's own
            // net48 SDK-style projects carry.
            yield return new XElement(
                "ItemGroup",
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "Microsoft.NETFramework.ReferenceAssemblies"),
                    new XAttribute("Version", "1.0.3"),
                    new XAttribute("PrivateAssets", "all")),
                // On net48, Span(Of T)/MemoryMarshal arrive through this package rather than the
                // shared framework -- the same version the corpus's own net48 VB family carries.
                new XElement(
                    "PackageReference",
                    new XAttribute("Include", "System.Memory"),
                    new XAttribute("Version", "4.5.5")));
        }

        if (projectReferences.Count > 0)
        {
            yield return new XElement(
                "ItemGroup",
                projectReferences.Select(projectReference => new XElement(
                    "ProjectReference",
                    new XAttribute("Include", projectReference))));
        }
    }

    private static XElement CompileItem(string path, string link) =>
        new(
            "Compile",
            new XAttribute("Include", path),
            new XAttribute("Link", link));

    private static string ProjectExtension(string sourcePath) =>
        Path.GetExtension(sourcePath) switch
        {
            ".cs" => "csproj",
            ".vb" => "vbproj",
            _ => throw new InvalidOperationException($"Unsupported probe source language: {sourcePath}."),
        };

    private static void EnsureSameLanguage(string sourcePath, string harnessPath)
    {
        var sourceExtension = Path.GetExtension(sourcePath);
        var harnessExtension = Path.GetExtension(harnessPath);
        if (!string.Equals(sourceExtension, harnessExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Harness language must match the source language: {sourcePath} vs {harnessPath}.");
        }
    }

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
