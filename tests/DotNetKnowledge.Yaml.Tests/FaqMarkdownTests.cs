using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Yaml.Tests;

[TestClass]
public sealed class FaqMarkdownTests
{
    private static FaqDocument Document(string? title, string? summary, params FaqSection[] sections) =>
        new(title, summary, sections);

    [TestMethod]
    public void RenderMakesSectionsLevelOneAndQuestionsLevelTwo()
    {
        var markdown = FaqMarkdown.Render(Document(
            "Widget frequently-asked questions",
            null,
            new FaqSection("General", [new FaqQuestion("How do I install a widget?", "Run the installer.")])));

        StringAssert.Contains(markdown, "# General");
        StringAssert.Contains(markdown, "## How do I install a widget?");
        StringAssert.Contains(markdown, "Run the installer.");
    }

    [TestMethod]
    public void RenderOmitsTheTitle()
    {
        // As an H1 the title becomes the ancestor of every heading, prefixing every section path
        // with the document's own name and making a two-level outline three levels deep. The
        // payload already carries document identity in `path`.
        var markdown = FaqMarkdown.Render(Document(
            "Widget frequently-asked questions",
            null,
            new FaqSection("General", [new FaqQuestion("Q?", "A.")])));

        Assert.IsFalse(markdown.Contains("Widget frequently-asked questions", StringComparison.Ordinal));
        Assert.IsFalse(markdown.StartsWith("# Widget", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RenderPutsTheSummaryBeforeTheFirstSection()
    {
        var markdown = FaqMarkdown.Render(Document(
            null,
            "Intro prose about widgets.",
            new FaqSection("General", [new FaqQuestion("Q?", "A.")])));

        Assert.IsTrue(
            markdown.IndexOf("Intro prose", StringComparison.Ordinal)
            < markdown.IndexOf("# General", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RenderFlattensAMultiLineQuestionToOneLine()
    {
        // question: | is a block scalar and may span lines. A multi-line "## " is not a heading at
        // all, and the outline would lose the entry entirely.
        var markdown = FaqMarkdown.Render(Document(
            null,
            null,
            new FaqSection("General", [new FaqQuestion("How do I\ninstall   a widget?", "A.")])));

        StringAssert.Contains(markdown, "## How do I install a widget?");
    }

    [TestMethod]
    public void RenderPreservesAnswerBodiesVerbatim()
    {
        const string answer =
            "Use the CLI:\n" +
            "\n" +
            "```bash\n" +
            "widget install\n" +
            "```\n" +
            "\n" +
            "> [!NOTE]\n" +
            "> See [the guide](../guides/widgets.md).";

        var markdown = FaqMarkdown.Render(Document(
            null, null, new FaqSection("General", [new FaqQuestion("Q?", answer)])));

        StringAssert.Contains(markdown, "```bash\nwidget install\n```");
        StringAssert.Contains(markdown, "> [!NOTE]");
        StringAssert.Contains(markdown, "[the guide](../guides/widgets.md)");
    }

    [TestMethod]
    public void RenderEmitsAHeadingForAQuestionWithNoAnswer()
    {
        var markdown = FaqMarkdown.Render(Document(
            null, null, new FaqSection("General", [new FaqQuestion("Q?", string.Empty)])));

        StringAssert.Contains(markdown, "## Q?");
    }

    [TestMethod]
    public void RenderSeparatesEveryBlockWithABlankLine()
    {
        // Markdig needs the blank line: "# General" immediately followed by prose still parses, but
        // two headings run together do not, and the outline would silently lose one.
        var markdown = FaqMarkdown.Render(Document(
            null,
            null,
            new FaqSection("General", [new FaqQuestion("Q1?", "A1."), new FaqQuestion("Q2?", "A2.")])));

        StringAssert.Contains(markdown, "# General\n\n## Q1?\n\nA1.\n\n## Q2?\n\nA2.\n");
    }
}
