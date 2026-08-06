using DotNetKnowledge.Corpus.Tests.Cases;

namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Integration")]
public sealed class RequiredToolchainsTests
{
    [TestMethod]
    public async Task EveryCheckedInCaseHasItsRequiredToolchains()
    {
        var cases = LoadCases();
        var inventory = await ToolchainInventory.Current;
        
        var missing = cases.SelectMany(testCase => testCase.Compilations)
            .Select(compilation => compilation.SdkBand)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ParseBand)
            .Select(sdkBand => MissingSdk(inventory, sdkBand))
            .OfType<string>()
            .ToList();

        missing.AddRange(from runtimeBand in cases.SelectMany(testCase => testCase.Runtimes)
                .Select(runtime => RuntimeBand(runtime.TargetFramework))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ParseBand)
            where !inventory.HasRuntime(runtimeBand)
            select $"Microsoft.NETCore.App runtime {runtimeBand}");

        if (missing.Count > 0)
        {
            Assert.Fail(
                $"Missing required toolchains:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missing.Select(toolchain => $"- {toolchain}"))
                + Environment.NewLine + Environment.NewLine
                + Remedy());
        }
    }

    /// <summary>
    /// Names the host that was inspected, then gives the remedy. Without the host path the failure
    /// reads as "those SDKs are not installed on this machine", while the far likelier cause is
    /// invoking the suite through the machine host, which never carries the older bands.
    /// <para>
    /// The message deliberately does not classify the host as private or machine-wide: there is no
    /// reliable test for that. DOTNET_HOST_PATH is set by <c>dotnet test</c> itself, so its presence
    /// says nothing, and the private root is relocatable via <c>--install-dir</c>. Reporting the
    /// path and letting the reader recognize it is the honest version.
    /// </para>
    /// </summary>
    private static string Remedy() =>
        $"""
         Inspected host: {ToolchainInventory.CurrentHostPath}

         If that is the machine-wide installation, it is the whole problem: it does not carry the
         older SDK bands this matrix compiles against, and it never will — that is why the corpus
         suite runs through a repository-private host. Install it and re-run through it:

             dotnet scripts/install-corpus-test-sdks.cs
             $env:DOTNET_HOST_PATH = (Resolve-Path .artifacts\dotnet\dotnet.exe)
             & $env:DOTNET_HOST_PATH test tests/DotNetKnowledge.Corpus.Tests/DotNetKnowledge.Corpus.Tests.csproj

         If it is already the private host, then these SDKs really are absent from it and the first
         command above installs them.

         `dotnet test Corpus.slnx` hits this too: a solution file does not select a host.
         """;

    private static CorpusCase[] LoadCases()
    {
        var caseDirectory = Path.Combine(AppContext.BaseDirectory, "TestCases");
        if (!Directory.Exists(caseDirectory))
        {
            throw new InvalidOperationException($"Corpus case directory does not exist: {caseDirectory}.");
        }

        var casePaths = Directory.GetFiles(caseDirectory, "*.json", SearchOption.AllDirectories);
        return CorpusCaseLoader.LoadValidated(casePaths, RepositoryRoot())
            .Select(document => document.Case)
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string? MissingSdk(ToolchainInventory inventory, string sdkBand)
    {
        try
        {
            _ = inventory.ResolveSdk(sdkBand);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static string RuntimeBand(string targetFramework)
    {
        const string netPrefix = "net";
        if (!targetFramework.StartsWith(netPrefix, StringComparison.Ordinal) ||
            targetFramework.Length == netPrefix.Length)
        {
            throw new InvalidOperationException($"Runtime target framework must use the net<major>.<minor> format: {targetFramework}.");
        }

        return targetFramework[netPrefix.Length..];
    }

    private static Version ParseBand(string band) => Version.Parse($"{band}.0");
}
