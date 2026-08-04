using System.Text.Json;
using DotNetKnowledge.CSharpScriptHost;

namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Unit")]
public sealed class ScenarioDescriptorLoaderTests
{
    [TestMethod]
    public void LoadRejectsUnknownMembers()
    {
        var path = WriteDescriptor(
            """
            {
              "id": "sample",
              "entry": "main.csx",
              "supportFiles": [],
              "hosts": ["api"],
              "arguments": [],
              "submissions": [],
              "expectations": {},
              "misspelled": true
            }
            """);

        Assert.ThrowsExactly<JsonException>(() => ScenarioDescriptorLoader.Load(path));
    }

    [TestMethod]
    [DataRow("supportFiles")]
    [DataRow("hosts")]
    [DataRow("arguments")]
    [DataRow("submissions")]
    [DataRow("expectations")]
    public void LoadRejectsOmittedRequiredCollections(string member)
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(DescriptorWithOmittedMember(member))));

        StringAssert.Contains(exception.Message, $"Required collection is missing: {member}.");
    }

    [TestMethod]
    public void LoadRejectsBlankIds()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(id: " "))));

        StringAssert.Contains(exception.Message, "Scenario ID is required.");
    }

    [TestMethod]
    public void LoadRejectsDuplicateHosts()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(hosts: "[\"api\", \"api\"]"))));

        StringAssert.Contains(exception.Message, "Duplicate host: api.");
    }

    [TestMethod]
    public void LoadRejectsEscapingEntryPaths()
    {
        using var scenario = new TemporaryScenario();

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(entry: "../escape.csx"))));

        StringAssert.Contains(exception.Message, "Path escapes the scenario directory: ../escape.csx.");
    }

    [TestMethod]
    public void LoadRejectsRootedEntryPaths()
    {
        using var scenario = new TemporaryScenario();
        var rootedEntry = Path.Combine(Path.GetPathRoot(scenario.DirectoryPath)!, "escape.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(entry: rootedEntry))));

        StringAssert.Contains(exception.Message, $"Path escapes the scenario directory: {rootedEntry}.");
    }

    [TestMethod]
    public void LoadRejectsMissingEntries()
    {
        using var scenario = new TemporaryScenario();

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor())));

        StringAssert.Contains(exception.Message, "Path does not exist: main.csx.");
    }

    [TestMethod]
    public void LoadRejectsUnknownHosts()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        Assert.ThrowsExactly<JsonException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(hosts: "[\"unknown\"]"))));
    }

    [TestMethod]
    public void LoadRejectsMissingExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(
                    ValidDescriptor(hosts: "[\"api\", \"csi\"]", expectations: "{ \"api\": {} }"))));

        StringAssert.Contains(exception.Message, "Missing expectation for host: csi.");
    }

    [TestMethod]
    public void LoadRejectsMismatchedExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(ValidDescriptor(expectations: "{ \"csi\": {} }"))));

        StringAssert.Contains(exception.Message, "Missing expectation for host: api.");
        StringAssert.Contains(exception.Message, "Unexpected expectation for host: csi.");
    }

    [TestMethod]
    public void LoadRejectsUnlistedSubmissions()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");
        scenario.WriteFile("continue.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(ValidDescriptor(submissions: "[\"main.csx\", \"continue.csx\"]"))));

        StringAssert.Contains(exception.Message, "Submission is not listed by entry or support files: continue.csx.");
    }

    [TestMethod]
    public void LoadRejectsNonScriptEntryAndSubmissionPaths()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.txt");
        scenario.WriteFile("continue.txt");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(
                    ValidDescriptor(
                        entry: "main.txt",
                        supportFiles: "[\"continue.txt\"]",
                        submissions: "[\"main.txt\", \"continue.txt\"]"))));

        StringAssert.Contains(exception.Message, "Entry must have a .csx extension: main.txt.");
        StringAssert.Contains(exception.Message, "Submission must have a .csx extension: main.txt.");
        StringAssert.Contains(exception.Message, "Submission must have a .csx extension: continue.txt.");
    }

    [TestMethod]
    public void LoadRequiresTheFirstSubmissionToMatchTheEntry()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");
        scenario.WriteFile("continue.csx");

        var exception = Assert.ThrowsExactly<InvalidDataException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(
                    ValidDescriptor(
                        supportFiles: "[\"continue.csx\"]",
                        submissions: "[\"continue.csx\", \"main.csx\"]"))));

        StringAssert.Contains(exception.Message, "The first submission must match entry: main.csx.");
    }

    private static string WriteDescriptor(string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-csx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "scenario.json");
        File.WriteAllText(path, contents);
        return path;
    }

    private static string ValidDescriptor(
        string id = "sample",
        string entry = "main.csx",
        string supportFiles = "[]",
        string hosts = "[\"api\"]",
        string submissions = "[]",
        string expectations = "{ \"api\": {} }") =>
        $$"""
        {
          "id": {{JsonSerializer.Serialize(id)}},
          "entry": {{JsonSerializer.Serialize(entry)}},
          "supportFiles": {{supportFiles}},
          "hosts": {{hosts}},
          "arguments": [],
          "submissions": {{submissions}},
          "expectations": {{expectations}}
        }
        """;

    private static string DescriptorWithOmittedMember(string omittedMember)
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = JsonSerializer.Serialize("sample"),
            ["entry"] = JsonSerializer.Serialize("main.csx"),
            ["supportFiles"] = "[]",
            ["hosts"] = "[\"api\"]",
            ["arguments"] = "[]",
            ["submissions"] = "[]",
            ["expectations"] = "{ \"api\": {} }"
        };
        if (!members.Remove(omittedMember))
        {
            throw new ArgumentOutOfRangeException(nameof(omittedMember));
        }

        var serializedMembers = string.Join(
            $",{Environment.NewLine}  ",
            members.Select(pair => $"\"{pair.Key}\": {pair.Value}"));
        return $"{{{Environment.NewLine}  {serializedMembers}{Environment.NewLine}}}";
    }

    private sealed class TemporaryScenario : IDisposable
    {
        public TemporaryScenario()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-csx-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string WriteDescriptor(string contents)
        {
            var path = Path.Combine(DirectoryPath, "scenario.json");
            File.WriteAllText(path, contents);
            return path;
        }

        public void WriteFile(string relativePath) =>
            File.WriteAllText(Path.Combine(DirectoryPath, relativePath), "// test fixture");

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
