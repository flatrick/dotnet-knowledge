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
        // The two support assemblies are not corpus projects and hold no feature rows. They sit
        // beside the corpus roots rather than inside one, and CorpusProjectBuildTests builds them
        // as shared references instead — so a discovery that reached them would build each twice.
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/CSharpComTypeLib/", StringComparison.Ordinal)));
        Assert.IsFalse(actual.Any(project => project.RepositoryRelativePath.Contains("/CSharpRefReturnLib/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FindSdkStyleNetFrameworkProjectsReturnsTheThreeSdkStyleProjectsInTheLegacyTree()
    {
        var repositoryRoot = RepositoryRoot();
        CorpusProject[] expected =
        [
            // Two of the three carry AllowUnsafeBlocks. This root takes them anyway: /unsafe is a
            // per-compilation switch, so the net48 tree houses those rows in their own project
            // rather than in a separate kind beside a library.
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v1.0-Unsafe/CSharp_v1.0-Unsafe.csproj", "net48", "1"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0-Unsafe/CSharp80Unsafe.csproj", "net48", "8.0"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v8.0/CSharp80.csproj", "net48", "8.0")
        ];

        var actual = CorpusProjectDiscovery.FindSdkStyleNetFrameworkProjects(repositoryRoot).ToArray();

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(3, actual.Length);
    }

    [TestMethod]
    public void FindLegacyNetFrameworkProjectsReturnsTheElevenNonSdkProjects()
    {
        var repositoryRoot = RepositoryRoot();
        CorpusProject[] expected =
        [
            // A legacy project names its framework with TargetFrameworkVersion, so the coordinate
            // reads v4.8 rather than net48. CSharp_v7.1-async_main is an Exe and is here for the
            // same reason the unsafe projects are above: it is a project the corpus authored, and
            // OutputType does not change what a build gate checks.
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v1.0/CSharp_v1.0.csproj", "v4.8", "1"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v2.0/CSharp_v2.0.csproj", "v4.8", "2"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v3.0/CSharp30.csproj", "v4.8", "3"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v4.0/CSharp40.csproj", "v4.8", "4"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v5.0/CSharp50.csproj", "v4.8", "5.0"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v6.0/CSharp60.csproj", "v4.8", "6.0"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.0/CSharp70.csproj", "v4.8", "7.0"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.1-async_main/CSharp7.1-async_main.csproj", "v4.8", "7.1"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.1/CSharp71.csproj", "v4.8", "7.1"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.2/CSharp72.csproj", "v4.8", "7.2"),
            new("examples/language-features/CSharp/dotNetFramework/v4.8/CSharp_v7.3/CSharp73.csproj", "v4.8", "7.3")
        ];

        var actual = CorpusProjectDiscovery.FindLegacyNetFrameworkProjects(repositoryRoot).ToArray();

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(11, actual.Length);
    }

    [TestMethod]
    public void TheNetFrameworkTreeIsCoveredByExactlyOneOfTheTwoNetFrameworkRoots()
    {
        var repositoryRoot = RepositoryRoot();
        var netFrameworkRoot = Path.Combine(
            repositoryRoot, "examples", "language-features", "CSharp", "dotNetFramework");

        var onDisk = Directory
            .EnumerateFiles(netFrameworkRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var covered = CorpusProjectDiscovery.FindSdkStyleNetFrameworkProjects(repositoryRoot)
            .Concat(CorpusProjectDiscovery.FindLegacyNetFrameworkProjects(repositoryRoot))
            .Select(project => project.RepositoryRelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(onDisk, covered);
        Assert.AreEqual(14, covered.Length);
    }

    [TestMethod]
    public void FindAllSdkStyleProjectsIsTheThreeRootsCombined()
    {
        var repositoryRoot = RepositoryRoot();

        var actual = CorpusProjectDiscovery.FindAllSdkStyleProjects(repositoryRoot);

        CollectionAssert.AreEqual(
            CorpusProjectDiscovery.FindSdkStyleLibraries(repositoryRoot)
                .Concat(CorpusProjectDiscovery.FindSdkStyleNetFrameworkProjects(repositoryRoot))
                .Concat(CorpusProjectDiscovery.FindSdkStyleVbProjects(repositoryRoot))
                .ToArray(),
            actual.ToArray());
        Assert.AreEqual(34, actual.Count);
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
