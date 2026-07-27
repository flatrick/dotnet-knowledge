namespace DotNetKnowledge.Corpus.Tests.Projects;

[TestClass]
[TestCategory("Unit")]
public sealed class CorpusProjectDiscoveryTests
{
    [TestMethod]
    public void FindSdkStyleLibrariesReturnsTheCompleteSortedCorpusMatrix()
    {
        var repositoryRoot = RepositoryRoot();
        CorpusProject[] expected =
        [
            new("examples/language-features/CSharp/dotnet/10/10.0/library/library.csproj", "net10.0", "10.0"),
            new("examples/language-features/CSharp/dotnet/10/11.0/library/library.csproj", "net10.0", "11.0"),
            new("examples/language-features/CSharp/dotnet/10/12.0/library/library.csproj", "net10.0", "12.0"),
            new("examples/language-features/CSharp/dotnet/10/13.0/library/library.csproj", "net10.0", "13.0"),
            new("examples/language-features/CSharp/dotnet/10/14.0/library/library.csproj", "net10.0", "14.0"),
            new("examples/language-features/CSharp/dotnet/10/latest/library/library.csproj", "net10.0", "latest"),
            new("examples/language-features/CSharp/dotnet/5.0/10.0/library/library.csproj", "net5.0", "10.0"),
            new("examples/language-features/CSharp/dotnet/6.0/10.0/library/library.csproj", "net6.0", "10.0"),
            new("examples/language-features/CSharp/dotnet/7.0/10.0/library/library.csproj", "net7.0", "10.0"),
            new("examples/language-features/CSharp/dotnet/8.0/10.0/library/library.csproj", "net8.0", "10.0"),
            new("examples/language-features/CSharp/dotnet/9.0/10.0/library/library.csproj", "net9.0", "10.0")
        ];

        var actual = CorpusProjectDiscovery.FindSdkStyleLibraries(repositoryRoot).ToArray();

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(11, actual.Length);
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/bin/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/obj/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/.artifacts/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/unsafe/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/exe/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/dotNetFramework/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/CSharpComTypeLib/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FindSdkStyleVbProjectsReturnsTheCompleteSortedVbMatrix()
    {
        var repositoryRoot = RepositoryRoot();
        CorpusProject[] expected =
        [
            // dotNetFramework sorts before dotnet: 'N' (U+004E) precedes 'n' (U+006E) ordinally.
            // Within v4.8, '.' (U+002E) sorts before '/' (U+002F), so 15.3 and 15.5 sort before 15,
            // and 16.9 before 16; within a pin, library/ sorts before my/.
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/11/library/library.vbproj", "net48", "11"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/11/my/my.vbproj", "net48", "11"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/14/library/library.vbproj", "net48", "14"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/15.3/library/library.vbproj", "net48", "15.3"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/15.5/library/library.vbproj", "net48", "15.5"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/15/library/library.vbproj", "net48", "15"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/16.9/library/library.vbproj", "net48", "16.9"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/16/library/library.vbproj", "net48", "16"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/17.13/library/library.vbproj", "net48", "17.13"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/latest/library/library.vbproj", "net48", "latest"),
            new("examples/language-features/VB.NET/dotNetFramework/v4.8/latest/my/my.vbproj", "net48", "latest"),
            new("examples/language-features/VB.NET/dotnet/Net10/11/library/library.vbproj", "net10.0", "11"),
            new("examples/language-features/VB.NET/dotnet/Net10/14/library/library.vbproj", "net10.0", "14"),
            new("examples/language-features/VB.NET/dotnet/Net10/15.3/library/library.vbproj", "net10.0", "15.3"),
            new("examples/language-features/VB.NET/dotnet/Net10/15.5/library/library.vbproj", "net10.0", "15.5"),
            new("examples/language-features/VB.NET/dotnet/Net10/15/library/library.vbproj", "net10.0", "15"),
            new("examples/language-features/VB.NET/dotnet/Net10/16.9/library/library.vbproj", "net10.0", "16.9"),
            new("examples/language-features/VB.NET/dotnet/Net10/16/library/library.vbproj", "net10.0", "16"),
            new("examples/language-features/VB.NET/dotnet/Net10/17.13/library/library.vbproj", "net10.0", "17.13"),
            new("examples/language-features/VB.NET/dotnet/Net10/latest/library/library.vbproj", "net10.0", "latest")
        ];

        var actual = CorpusProjectDiscovery.FindSdkStyleVbProjects(repositoryRoot).ToArray();

        CollectionAssert.AreEqual(expected, actual);
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/bin/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/obj/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FindSdkStyleLibrariesUsesProjectPropertiesInsteadOfFolderNames()
    {
        var repositoryRoot = CreateRepository();

        try
        {
            _ = CreateProject(
                repositoryRoot,
                "unexpected/location/corpus.csproj",
                """
                <TargetFramework>net7.0</TargetFramework>
                <LangVersion>12.0</LangVersion>
                """);
            _ = CreateProject(
                repositoryRoot,
                "looks/like/a/library.csproj",
                """
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <OutputType>Exe</OutputType>
                """);
            _ = CreateProject(
                repositoryRoot,
                "another/apparent/library.csproj",
                """
                <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
                <LangVersion>13.0</LangVersion>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                """);
            _ = CreateProject(
                repositoryRoot,
                "obj/generated/library.csproj",
                """
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                """);

            var actual = CorpusProjectDiscovery.FindSdkStyleLibraries(repositoryRoot);

            CollectionAssert.AreEqual(
                new[]
                {
                    new CorpusProject(
                        "examples/language-features/CSharp/dotnet/unexpected/location/corpus.csproj",
                        "net7.0",
                        "12.0")
                },
                actual.ToArray());
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("<LangVersion>latest</LangVersion>")]
    [DataRow("<TargetFramework>net10.0</TargetFramework>")]
    public void FindSdkStyleLibrariesRejectsMissingCoordinateValues(string projectProperties)
    {
        var repositoryRoot = CreateRepository();

        try
        {
            var projectPath = CreateProject(repositoryRoot, "missing/library.csproj", projectProperties);

            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => CorpusProjectDiscovery.FindSdkStyleLibraries(repositoryRoot));

            StringAssert.Contains(exception.Message, projectPath);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(
        """
        <TargetFramework>net10.0</TargetFramework>
        <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
        <LangVersion>latest</LangVersion>
        """)]
    [DataRow(
        """
        <TargetFramework>net10.0</TargetFramework>
        <LangVersion>13.0</LangVersion>
        <LangVersion>latest</LangVersion>
        """)]
    public void FindSdkStyleLibrariesRejectsContradictoryCoordinateValues(string projectProperties)
    {
        var repositoryRoot = CreateRepository();

        try
        {
            var projectPath = CreateProject(repositoryRoot, "contradictory/library.csproj", projectProperties);

            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => CorpusProjectDiscovery.FindSdkStyleLibraries(repositoryRoot));

            StringAssert.Contains(exception.Message, projectPath);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static string CreateRepository()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryRoot);
        return repositoryRoot;
    }

    private static string CreateProject(string repositoryRoot, string relativePath, string properties)
    {
        var projectPath = Path.Combine(
            repositoryRoot,
            "examples",
            "language-features",
            "CSharp",
            "dotnet",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                {properties}
              </PropertyGroup>
            </Project>
            """);
        return projectPath;
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
}
