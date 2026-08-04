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
        using var scenario = new TemporaryScenario();
        var path = scenario.WriteDescriptor(
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

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
        StringAssert.Contains(exception.Message, "misspelled");
    }

    [TestMethod]
    public void LoadWrapsDescriptorReadErrorsWithTheCanonicalPath()
    {
        using var scenario = new TemporaryScenario();
        var path = Path.Combine(scenario.DirectoryPath, "missing.json");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        Assert.AreEqual(Path.GetFullPath(path), exception.DescriptorPath);
        StringAssert.Contains(exception.Message, Path.GetFullPath(path));
        Assert.IsInstanceOfType<FileNotFoundException>(exception.InnerException);
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

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(DescriptorWithOmittedMember(member))));

        StringAssert.Contains(exception.Message, $"Required collection is missing: {member}.");
    }

    [TestMethod]
    [DataRow("supportFiles")]
    [DataRow("hosts")]
    [DataRow("arguments")]
    [DataRow("submissions")]
    [DataRow("expectations")]
    public void LoadRejectsNullRequiredCollections(string member)
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(DescriptorWithNullMember(member))));

        StringAssert.Contains(exception.Message, $"Required collection is missing: {member}.");
    }

    [TestMethod]
    public void LoadRejectsBlankIds()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(id: " "))));

        StringAssert.Contains(exception.Message, "Scenario ID is required.");
    }

    [TestMethod]
    public void LoadRejectsDuplicateHosts()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(hosts: "[\"api\", \"api\"]"))));

        StringAssert.Contains(exception.Message, "Duplicate host: api.");
    }

    [TestMethod]
    public void LoadRejectsEscapingEntryPaths()
    {
        using var scenario = new TemporaryScenario();

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(entry: "../escape.csx"))));

        StringAssert.Contains(exception.Message, "Path escapes the scenario directory: ../escape.csx.");
    }

    [TestMethod]
    public void LoadRejectsRootedEntryPaths()
    {
        using var scenario = new TemporaryScenario();
        var rootedEntry = Path.Combine(Path.GetPathRoot(scenario.DirectoryPath)!, "escape.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(entry: rootedEntry))));

        StringAssert.Contains(exception.Message, $"Path escapes the scenario directory: {rootedEntry}.");
    }

    [TestMethod]
    public void LoadRejectsMissingEntries()
    {
        using var scenario = new TemporaryScenario();

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor())));

        StringAssert.Contains(exception.Message, "Path does not exist: main.csx.");
    }

    [TestMethod]
    public void LoadRejectsUnknownHosts()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor(hosts: "[\"unknown\"]"))));
    }

    [TestMethod]
    public void LoadRejectsNumericHostKinds()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var path = scenario.WriteDescriptor(ValidDescriptor(hosts: "[0]"));
        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
    }

    [TestMethod]
    public void LoadRejectsEmptyHostExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var path = scenario.WriteDescriptor(ValidDescriptor(expectations: "{ \"api\": {} }"));
        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
    }

    [TestMethod]
    [DataRow("exitCode")]
    [DataRow("returnType")]
    [DataRow("returnValue")]
    [DataRow("standardOutput")]
    [DataRow("standardError")]
    [DataRow("completedSubmissionCount")]
    public void LoadRejectsOmittedHostExpectationMembers(string member)
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var path = scenario.WriteDescriptor(
            ValidDescriptor(expectations: ExpectationWithOmittedMember("api", member)));
        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
        StringAssert.Contains(exception.Message, member);
    }

    [TestMethod]
    public void LoadRejectsNullHostExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var path = scenario.WriteDescriptor(ValidDescriptor(expectations: "{ \"api\": null }"));
        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
        StringAssert.Contains(exception.Message, "Expectation must not be null: api.");
    }

    [TestMethod]
    [DataRow("standardOutput")]
    [DataRow("standardError")]
    public void LoadRejectsNullHostExpectationCollections(string member)
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var path = scenario.WriteDescriptor(
            ValidDescriptor(expectations: ExpectationWithMember("api", member, "null")));
        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        StringAssert.Contains(exception.Message, path);
        StringAssert.Contains(exception.Message, $"Expectation collection must not be null: api.{member}.");
    }

    [TestMethod]
    public void LoadAcceptsExplicitNullReturnMembers()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var descriptor = ScenarioDescriptorLoader.Load(scenario.WriteDescriptor(ValidDescriptor()));
        var expectation = descriptor.Expectations[ScriptHostKind.Api];

        Assert.IsNull(expectation.ReturnType);
        Assert.IsNull(expectation.ReturnValue);
    }

    [TestMethod]
    public void LoadRejectsMissingExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(
                    ValidDescriptor(hosts: "[\"api\", \"csi\"]", expectations: ValidExpectation("api")))));

        StringAssert.Contains(exception.Message, "Missing expectation for host: csi.");
    }

    [TestMethod]
    public void LoadRejectsMismatchedExpectations()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(ValidDescriptor(expectations: ValidExpectation("csi")))));

        StringAssert.Contains(exception.Message, "Missing expectation for host: api.");
        StringAssert.Contains(exception.Message, "Unexpected expectation for host: csi.");
    }

    [TestMethod]
    public void LoadRejectsUnlistedSubmissions()
    {
        using var scenario = new TemporaryScenario();
        scenario.WriteFile("main.csx");
        scenario.WriteFile("continue.csx");

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
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

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
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

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(
                scenario.WriteDescriptor(
                    ValidDescriptor(
                        supportFiles: "[\"continue.csx\"]",
                        submissions: "[\"continue.csx\", \"main.csx\"]"))));

        StringAssert.Contains(exception.Message, "The first submission must match entry: main.csx.");
    }

    [TestMethod]
    public void LoadAggregatesSemanticErrorsInOrdinalOrder()
    {
        using var scenario = new TemporaryScenario();
        var path = scenario.WriteDescriptor(
            ValidDescriptor(
                id: " ",
                hosts: "[\"api\", \"api\"]",
                expectations: "{}"));

        var exception = Assert.ThrowsExactly<ScenarioDescriptorValidationException>(() =>
            ScenarioDescriptorLoader.Load(path));

        Assert.AreEqual(
            $"Scenario descriptor is invalid: {path}{Environment.NewLine}" +
            $"- Duplicate host: api.{Environment.NewLine}" +
            $"- Missing expectation for host: api.{Environment.NewLine}" +
            $"- Path does not exist: main.csx.{Environment.NewLine}" +
            "- Scenario ID is required.",
            exception.Message);
    }

    private static string ValidDescriptor(
        string id = "sample",
        string entry = "main.csx",
        string supportFiles = "[]",
        string hosts = "[\"api\"]",
        string submissions = "[]",
        string? expectations = null)
    {
        expectations ??= ValidExpectation("api");
        return $$"""
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
    }

    private static string ValidExpectation(string host) =>
        $$"""
        {
          {{JsonSerializer.Serialize(host)}}: {
            "exitCode": 0,
            "returnType": null,
            "returnValue": null,
            "standardOutput": [],
            "standardError": [],
            "completedSubmissionCount": 1
          }
        }
        """;

    private static string ExpectationWithOmittedMember(string host, string omittedMember)
    {
        var members = ExpectationMembers();
        if (!members.Remove(omittedMember))
        {
            throw new ArgumentOutOfRangeException(nameof(omittedMember));
        }

        return SerializeExpectation(host, members);
    }

    private static string ExpectationWithMember(string host, string member, string value)
    {
        var members = ExpectationMembers();
        if (!members.ContainsKey(member))
        {
            throw new ArgumentOutOfRangeException(nameof(member));
        }

        members[member] = value;
        return SerializeExpectation(host, members);
    }

    private static Dictionary<string, string> ExpectationMembers() => new(StringComparer.Ordinal)
    {
        ["exitCode"] = "0",
        ["returnType"] = "null",
        ["returnValue"] = "null",
        ["standardOutput"] = "[]",
        ["standardError"] = "[]",
        ["completedSubmissionCount"] = "1"
    };

    private static string SerializeExpectation(string host, IReadOnlyDictionary<string, string> members)
    {
        var serializedMembers = string.Join(", ", members.Select(pair =>
            $"{JsonSerializer.Serialize(pair.Key)}: {pair.Value}"));
        return $"{{ {JsonSerializer.Serialize(host)}: {{ {serializedMembers} }} }}";
    }

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
            ["expectations"] = ValidExpectation("api")
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

    private static string DescriptorWithNullMember(string nullMember)
    {
        var members = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = JsonSerializer.Serialize("sample"),
            ["entry"] = JsonSerializer.Serialize("main.csx"),
            ["supportFiles"] = "[]",
            ["hosts"] = "[\"api\"]",
            ["arguments"] = "[]",
            ["submissions"] = "[]",
            ["expectations"] = ValidExpectation("api")
        };
        if (!members.ContainsKey(nullMember))
        {
            throw new ArgumentOutOfRangeException(nameof(nullMember));
        }

        members[nullMember] = "null";
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
