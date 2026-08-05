using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownOutlineTests
{
    private const string SampleDocument =
        "# Title\n" +
        "\n" +
        "## A\n" +
        "\n" +
        "Some prose in A.\n" +
        "\n" +
        "### B\n" +
        "\n" +
        "Nested under B.\n" +
        "\n" +
        "## C\n" +
        "\n" +
        "## A\n" +
        "\n" +
        "Repeated heading text.\n";

    [TestMethod]
    public void ExtractBuildsAncestorPathsAndLineRanges()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        Assert.HasCount(5, headings);
        Assert.AreEqual("Title", headings[0].Path);
        Assert.AreEqual(1, headings[0].Level);
        Assert.AreEqual(1, headings[0].StartLine);
        Assert.AreEqual("Title > A", headings[1].Path);
        Assert.AreEqual("Title > A > B", headings[2].Path);
        Assert.AreEqual("Title > C", headings[3].Path);
    }

    [TestMethod]
    public void ExtractSuffixesOnlyTheCollidingPath()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        // "## A" occurs twice as a direct child of "Title": the first keeps its plain path,
        // the second collides and gets a suffix. Every other path is untouched.
        Assert.AreEqual("Title > A", headings[1].Path);
        Assert.AreEqual("Title > A (2)", headings[4].Path);
    }

    [TestMethod]
    public void ExtractComputesExclusiveEndLinesFromTheNextSameOrHigherHeading()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        var sectionA = headings.Single(h => h.Path == "Title > A");
        var sectionB = headings.Single(h => h.Path == "Title > A > B");
        var sectionC = headings.Single(h => h.Path == "Title > C");
        var sectionALast = headings.Single(h => h.Path == "Title > A (2)");

        // "## A" ends where the next same-or-higher heading, "## C", begins: "### B" nests inside
        // A (a lower level does not close it), so A's own range extends past B to C.
        Assert.AreEqual(sectionC.StartLine, sectionA.EndLine);
        // "### B" ends where "## C" begins too - the next heading at any level closes a childless one.
        Assert.AreEqual(sectionC.StartLine, sectionB.EndLine);
        // The last heading in the document ends one past the last line.
        var totalLines = SampleDocument.Split('\n').Length;
        Assert.AreEqual(totalLines + 1, sectionALast.EndLine);
    }

    [TestMethod]
    public void ExtractIgnoresAHeadingMarkerInsideAFencedCodeBlock()
    {
        const string document = "# Title\n\n```\n# not a heading\n```\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.HasCount(1, headings);
        Assert.AreEqual("Title", headings[0].Path);
    }

    [TestMethod]
    public void ExtractHandlesASetextHeading()
    {
        const string document = "Setext Heading\n--------------\n\nBody text.\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.HasCount(1, headings);
        Assert.AreEqual(2, headings[0].Level);
        Assert.AreEqual("Setext Heading", headings[0].Text);
        Assert.AreEqual(1, headings[0].StartLine);
    }

    [TestMethod]
    public void ExtractStripsInlineFormattingFromHeadingText()
    {
        const string document = "## Sub `Heading` with *emphasis*\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.AreEqual("Sub Heading with emphasis", headings[0].Text);
    }

    [TestMethod]
    public void ExtractLineNumbersAgreeWithNormalizedLineSplittingWhenTheDocumentHasAFormFeed()
    {
        // A raw '\n'-only count and ReplaceLineEndings's normalized count disagree once a form feed
        // (or a bare '\r', or a Unicode NEL/LS/PS) occurs: normalization treats it as a line break,
        // naive '\n'-counting does not. Every consumer of StartLine/EndLine — MarkdownLineSearch and
        // LanguageDocsQueryService.GetDocAsync — builds its line array via
        // markdown.ReplaceLineEndings("\n").Split('\n'), so headings must agree with that split or
        // section slicing silently returns the wrong lines.
        const string document =
            "# Title\n" +
            "\n" +
            "Prose beforethe form feed.\n" +
            "\n" +
            "## Heading After\n" +
            "\n" +
            "Body.\n";

        var headings = MarkdownOutline.Extract(document);
        var normalizedLines = document.ReplaceLineEndings("\n").Split('\n');

        var after = headings.Single(h => h.Text == "Heading After");
        Assert.AreEqual("## Heading After", normalizedLines[after.StartLine - 1]);
        Assert.AreEqual(6, after.StartLine);
        Assert.AreEqual(normalizedLines.Length + 1, after.EndLine);
    }
}
