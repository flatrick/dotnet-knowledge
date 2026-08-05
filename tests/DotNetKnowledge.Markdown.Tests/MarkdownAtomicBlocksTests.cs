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
}
