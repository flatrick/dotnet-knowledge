using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownAtomicBlocksTests
{
    // Line numbers (1-based):
    //  1: # Title            9: ```csharp         16: | X | Y |
    //  2:                   10: class Foo         17: |---|---|
    //  3: ## A               11: {                18: | 1 | 2 |
    //  4:                   12:     void Bar() { } 19:
    //  5: ### B              13: }                 20: Tail line.
    //  6:                   14: ```
    //  7: ## C               15:
    //  8:
    private const string Document =
        "# Title\n\n## A\n\n### B\n\n## C\n\n" +
        "```csharp\nclass Foo\n{\n    void Bar() { }\n}\n```\n\n" +
        "| X | Y |\n|---|---|\n| 1 | 2 |\n\nTail line.\n";

    [TestMethod]
    public void FindReturnsTheFencedCodeBlockAsAnExclusiveEndRange()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        var fenced = blocks.Single(b => b.StartLine == 9);
        Assert.AreEqual(15, fenced.EndLine);
    }

    [TestMethod]
    public void FindReturnsTheTableAsAnExclusiveEndRange()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        var table = blocks.Single(b => b.StartLine == 16);
        Assert.AreEqual(19, table.EndLine);
    }

    [TestMethod]
    public void FindReturnsBlocksOrderedByStartLine()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        CollectionAssert.AreEqual(
            blocks.OrderBy(b => b.StartLine).ToArray(),
            blocks.ToArray());
    }

    [TestMethod]
    public void FindHandlesHeaderOnlyTableCorrectly()
    {
        // A table with only header and separator rows (no data rows) must still cover the separator.
        // Line numbers (1-based):
        //  1: Before.
        //  2:
        //  3: | X | Y |
        //  4: |---|---|
        //  5:
        //  6: After.
        var document = "Before.\n\n| X | Y |\n|---|---|\n\nAfter.\n";
        var blocks = MarkdownAtomicBlocks.Find(document);

        var table = blocks.Single(b => b.StartLine == 3);
        Assert.AreEqual(5, table.EndLine);
    }

    [TestMethod]
    public void FindTreatsYamlFrontMatterAsAtomic()
    {
        const string document =
            "---\n" +
            "title: Sample\n" +
            "ms.author: someone\n" +
            "ms.date: 02/12/2026\n" +
            "---\n" +
            "\n" +
            "# Heading One\n" +
            "\n" +
            "Body text.\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        // Opening "---" is line 1, content is lines 2-4, closing "---" is line 5, and EndLine is
        // exclusive — so the range is [1, 6).
        Assert.HasCount(1, blocks);
        Assert.AreEqual(1, blocks[0].StartLine);
        Assert.AreEqual(6, blocks[0].EndLine);
    }

    [TestMethod]
    public void FindTreatsMinimalYamlFrontMatterAsAtomic()
    {
        // The smallest input Markdig 1.3.2 actually parses as front matter: one content line,
        // even a blank one. This pins the arithmetic at its lower boundary.
        const string document =
            "---\n" +
            "\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(1, blocks[0].StartLine);
        Assert.AreEqual(4, blocks[0].EndLine);
    }

    [TestMethod]
    public void FindDoesNotTreatAdjacentFencesAsFrontMatter()
    {
        // Measured against Markdig 1.3.2: "---" on adjacent lines is two thematic breaks, not an
        // empty front-matter block. This is the boundary the zero-lines guard in Find defends, and
        // it is why that guard is never reached today.
        const string document =
            "---\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        Assert.IsEmpty(blocks);
    }
}
