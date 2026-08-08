namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownFrontMatterTests
{
    // The shape every Microsoft Learn article has: front matter on lines 1-5, a blank line 6,
    // and the real heading on line 7.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "---\n" +
        "\n" +
        "# Heading One\n" +
        "\n" +
        "Body text.\n";

    [TestMethod]
    public void BodyStartLineSkipsFrontMatterAndTheBlankLineAfterIt()
    {
        Assert.AreEqual(7, MarkdownFrontMatter.BodyStartLine(LearnArticle));
    }

    [TestMethod]
    public void BodyStartLineIsOneWhenThereIsNoFrontMatter()
    {
        const string document =
            "# Heading One\n" +
            "\n" +
            "Body text.\n";

        Assert.AreEqual(1, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLineTakesTheContentLineImmediatelyAfterTheClosingFence()
    {
        const string document =
            "---\n" +
            "title: Sample\n" +
            "---\n" +
            "# Heading One\n";

        Assert.AreEqual(4, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLinePointsPastTheEndWhenFrontMatterHasNoBody()
    {
        // No such file exists in nuget-docs today. Without this, a document that is only metadata
        // would make get_doc index outside the line array.
        const string document =
            "---\n" +
            "title: Sample\n" +
            "---\n";

        // Four lines after the trailing-newline split: "---", "title: Sample", "---", "".
        Assert.AreEqual(5, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLineIsOneForAdjacentFencesWhichAreNotFrontMatter()
    {
        // Measured against Markdig 1.3.2: "---" on adjacent lines is two thematic breaks, not an
        // empty front-matter block, so the document starts at line 1 like any other.
        const string document =
            "---\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        Assert.AreEqual(1, MarkdownFrontMatter.BodyStartLine(document));
    }
}
