using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Yaml.Tests;

[TestClass]
public sealed class FaqDocumentTests
{
    // The shape Microsoft Learn's structured-FAQ schema uses: a metadata block that is not content,
    // an optional summary, then sections of question-and-answer pairs as block scalars.
    private const string NominalFaq =
        "### YamlMime:FAQ\n" +
        "metadata:\n" +
        "  title: Widget FAQ\n" +
        "  ms.author: someone\n" +
        "  ms.date: 01/08/2026\n" +
        "title: Widget frequently-asked questions\n" +
        "summary: |\n" +
        "  Intro prose about widgets.\n" +
        "sections:\n" +
        "  - name: General\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          How do I install a widget?\n" +
        "        answer: |\n" +
        "          Run the installer.\n" +
        "      - question: |\n" +
        "          Where do widgets live?\n" +
        "        answer: |\n" +
        "          In the widget directory.\n" +
        "  - name: Troubleshooting\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          Why did my widget fail?\n" +
        "        answer: |\n" +
        "          Check the log.\n";

    [TestMethod]
    public void ParseReadsTitleSummarySectionsAndQuestions()
    {
        var document = FaqDocument.Parse(NominalFaq);

        Assert.AreEqual("Widget frequently-asked questions", document.Title);
        StringAssert.Contains(document.Summary, "Intro prose about widgets.");
        Assert.AreEqual(2, document.Sections.Count);
        Assert.AreEqual("General", document.Sections[0].Name);
        Assert.AreEqual(2, document.Sections[0].Questions.Count);
        Assert.AreEqual("Troubleshooting", document.Sections[1].Name);
        Assert.AreEqual(1, document.Sections[1].Questions.Count);
        StringAssert.Contains(document.Sections[0].Questions[0].Question, "How do I install a widget?");
        StringAssert.Contains(document.Sections[0].Questions[0].Answer, "Run the installer.");
    }

    [TestMethod]
    public void ParseDoesNotCarryMetadata()
    {
        // metadata is about the document, not part of it - the same call front matter gets.
        var document = FaqDocument.Parse(NominalFaq);

        Assert.AreEqual("Widget frequently-asked questions", document.Title);
        Assert.IsFalse(document.Sections.Any(section =>
            section.Questions.Any(question => question.Answer.Contains("ms.author", StringComparison.Ordinal))));
    }

    [TestMethod]
    public void ParseAcceptsAnAbsentSummaryAndTitle()
    {
        // nuget-org-faq.yml carries no summary. That is a real document, not a broken one.
        var document = FaqDocument.Parse(
            "### YamlMime:FAQ\n" +
            "sections:\n" +
            "  - name: Only\n" +
            "    questions:\n" +
            "      - question: |\n" +
            "          Q?\n" +
            "        answer: |\n" +
            "          A.\n");

        Assert.IsNull(document.Title);
        Assert.IsNull(document.Summary);
        Assert.AreEqual(1, document.Sections.Count);
    }

    [TestMethod]
    public void ParseAcceptsAnEmptyAnswer()
    {
        var document = FaqDocument.Parse(
            "### YamlMime:FAQ\n" +
            "sections:\n" +
            "  - name: Only\n" +
            "    questions:\n" +
            "      - question: |\n" +
            "          Q?\n");

        Assert.AreEqual(string.Empty, document.Sections[0].Questions[0].Answer);
    }

    [TestMethod]
    public void ParseRejectsADocumentWithNoSections()
    {
        // YamlDotNet's IgnoreUnmatchedProperties turns a wholly unrecognized document into all-null
        // properties and reports success. Reporting that as "a FAQ with no content" is exactly the
        // silent absence this feature exists to remove.
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse("### YamlMime:FAQ\nunrelated: value\n"));

        StringAssert.Contains(exception.Message, "no sections");
    }

    [TestMethod]
    public void ParseRejectsASectionWithNoQuestions()
    {
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse(
                "### YamlMime:FAQ\n" +
                "sections:\n" +
                "  - name: Empty\n"));

        StringAssert.Contains(exception.Message, "Empty");
    }

    [TestMethod]
    public void ParseRejectsMalformedYaml()
    {
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse(
                "### YamlMime:FAQ\n" +
                "sections:\n" +
                "  - name: Broken\n" +
                "   questions: [\n"));

        Assert.IsNotNull(exception.Message);
    }
}
