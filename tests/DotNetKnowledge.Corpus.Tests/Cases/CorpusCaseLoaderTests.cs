namespace DotNetKnowledge.Corpus.Tests.Cases;

[TestClass]
[TestCategory("Unit")]
public sealed class CorpusCaseLoaderTests
{
    [TestMethod]
    public void LoadReadsValidCase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid-case.json");

        var testCase = CorpusCaseLoader.Load(path);

        Assert.AreEqual("Harness.Valid", testCase.Id);
        Assert.AreEqual("fixtures/Valid.cs", testCase.Source);
        Assert.HasCount(1, testCase.Compilations);
        Assert.AreEqual("10.0", testCase.Compilations[0].SdkBand);
        Assert.AreEqual("net10.0", testCase.Compilations[0].TargetFramework);
        Assert.AreEqual("14.0", testCase.Compilations[0].LanguageVersion);
        Assert.AreEqual(BuildOutcome.Success, testCase.Compilations[0].Outcome);
    }

    [TestMethod]
    [DataRow("missing-id", "Case ID is required.")]
    [DataRow("duplicate-compilation", "Duplicate compilation coordinate: 10.0|net10.0|14.0.")]
    [DataRow("failure-without-diagnostic", "Failure compilation 10.0|net10.0|13.0 must name at least one diagnostic.")]
    [DataRow("failure-with-blank-diagnostic", "Failure compilation 10.0|net10.0|13.0 must not name blank diagnostics.")]
    [DataRow("runtime-without-success", "Runtime coordinate 10.0|net10.0|14.0 must have a successful compilation expectation.")]
    [DataRow("missing-source", "Source does not exist: fixtures/Missing.cs.")]
    public void ValidateReportsExpectedError(string scenario, string expectedError)
    {
        var testCase = scenario switch
        {
            "missing-id" => CreateCase(id: " "),
            "duplicate-compilation" => CreateCase(compilations:
            [
                SuccessfulCompilation(),
                SuccessfulCompilation()
            ]),
            "failure-without-diagnostic" => CreateCase(compilations:
            [
                new CompilationExpectation("10.0", "net10.0", "13.0", BuildOutcome.Failure, [])
            ]),
            "failure-with-blank-diagnostic" => CreateCase(compilations:
            [
                new CompilationExpectation("10.0", "net10.0", "13.0", BuildOutcome.Failure, ["CS0001", " "])
            ]),
            "runtime-without-success" => CreateCase(
                compilations: [new CompilationExpectation("10.0", "net10.0", "14.0", BuildOutcome.Failure, ["CS0001"])],
                runtimes: [new RuntimeExpectation("Fixtures/valid-case.json", "10.0", "net10.0", "14.0", 0, [])]),
            "missing-source" => CreateCase(source: "fixtures/Missing.cs"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var errors = testCase.Validate(AppContext.BaseDirectory);

        CollectionAssert.AreEqual(new[] { expectedError }, errors.ToArray());
    }

    private static CorpusCase CreateCase(
        string id = "Harness.Valid",
        string source = "Fixtures/valid-case.json",
        IReadOnlyList<CompilationExpectation>? compilations = null,
        IReadOnlyList<RuntimeExpectation>? runtimes = null) =>
        new(id, source, compilations ?? [SuccessfulCompilation()], runtimes ?? []);

    private static CompilationExpectation SuccessfulCompilation() =>
        new("10.0", "net10.0", "14.0", BuildOutcome.Success, []);
}
