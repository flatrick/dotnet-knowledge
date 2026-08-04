using DotNetKnowledge.CSharpScriptHost;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Unit")]
public sealed class CSharpScriptManifestTests
{
    private const string Heading = "## C# scripts (`.csx`)";
    private const string Header = "| Scenario | Entry | Hosts | Demonstrates | Note |";
    private const string Separator = "|---|---|---|---|---|";

    [TestMethod]
    public void LoadReadsOnlyTheExactScriptTable()
    {
        using var manifest = WriteManifest(
            $"""
            # Corpus

            {Heading}

            {Header}
            {Separator}
            | sample | `CSharp/csx/roslyn-5.6.0/examples/sample/main.csx` | `api`, `csi` | Final expressions | |

            ## Visual Basic

            | Feature | Version |
            |---|---|
            | ignored | 14 |
            """);

        var rows = CSharpScriptManifest.Load(manifest.Path);
        Assert.HasCount(1, rows);
        var row = rows[0];

        Assert.AreEqual("sample", row.Id);
        Assert.AreEqual("CSharp/csx/roslyn-5.6.0/examples/sample/main.csx", row.Entry);
        CollectionAssert.AreEqual(
            new[] { ScriptHostKind.Api, ScriptHostKind.Csi },
            row.Hosts.ToArray());
        Assert.AreEqual("Final expressions", row.Demonstrates);
        Assert.AreEqual(string.Empty, row.Note);
    }

    [TestMethod]
    public void LoadRejectsDuplicateScenarioIdsAtTheDuplicateLine()
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {Header}
            {Separator}
            | sample | first.csx | api | First | |
            | sample | second.csx | api | Second | |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 6");
        StringAssert.Contains(exception.Message, "Duplicate script scenario ID 'sample'");
    }

    [TestMethod]
    public void LoadRejectsMalformedColumnCountsAtTheRowLine()
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {Header}
            {Separator}
            | sample | main.csx | api | Missing note |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 5");
        StringAssert.Contains(exception.Message, "exactly five columns");
    }

    [TestMethod]
    public void LoadRejectsUnknownHostsAtTheRowLine()
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {Header}
            {Separator}
            | sample | main.csx | api, notebook | Hosts | |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 5");
        StringAssert.Contains(exception.Message, "Unknown script host 'notebook'");
    }

    [TestMethod]
    public void LoadRejectsMissingTableAtTheNextSectionLine()
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            ## Visual Basic
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 3");
        StringAssert.Contains(exception.Message, "script table header");
    }

    [TestMethod]
    public void LoadRejectsContentAfterTheTableAtTheContentLine()
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {Header}
            {Separator}
            | sample | main.csx | api | Hosts | |

            This prose does not belong inside the script inventory.

            ## Visual Basic
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 7");
        StringAssert.Contains(exception.Message, "Unexpected content after the script table");
    }

    [TestMethod]
    [DataRow("", "main.csx", "api", "Hosts", "Scenario")]
    [DataRow("sample", "", "api", "Hosts", "Entry")]
    [DataRow("sample", "main.csx", "", "Hosts", "Hosts")]
    [DataRow("sample", "main.csx", "api", "", "Demonstrates")]
    public void LoadRejectsBlankRequiredColumns(
        string id,
        string entry,
        string hosts,
        string demonstrates,
        string expectedColumn)
    {
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {Header}
            {Separator}
            | {id} | {entry} | {hosts} | {demonstrates} | |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 5");
        StringAssert.Contains(exception.Message, $"column '{expectedColumn}' must not be blank");
    }

    [TestMethod]
    public void LoadRejectsAHeadingThatIsNotExact()
    {
        using var manifest = WriteManifest(
            $"""
            ## C# scripts

            {Header}
            {Separator}
            | sample | main.csx | api | Hosts | |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, "line 1");
        StringAssert.Contains(exception.Message, "Required heading is missing");
    }

    [TestMethod]
    [DataRow("| Scenario | Entry | Host | Demonstrates | Note |", "header", 3)]
    [DataRow("| --- | --- | --- | --- | --- |", "separator", 4)]
    public void LoadRequiresTheExactHeaderAndSeparator(string replacement, string kind, int expectedLine)
    {
        var header = kind == "header" ? replacement : Header;
        var separator = kind == "separator" ? replacement : Separator;
        using var manifest = WriteManifest(
            $"""
            {Heading}

            {header}
            {separator}
            | sample | main.csx | api | Hosts | |
            """);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => CSharpScriptManifest.Load(manifest.Path));

        StringAssert.Contains(exception.Message, $"line {expectedLine}");
        StringAssert.Contains(exception.Message, $"script table {kind}");
    }

    private static TemporaryManifest WriteManifest(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-manifest-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, contents);
        return new TemporaryManifest(path);
    }

    private sealed class TemporaryManifest(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose() => File.Delete(Path);
    }
}
