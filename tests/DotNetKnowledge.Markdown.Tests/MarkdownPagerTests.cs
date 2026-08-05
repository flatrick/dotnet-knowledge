using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownPagerTests
{
    [TestMethod]
    public void PageStopsAtABudgetOnAnOrdinaryLineBoundary()
    {
        var lines = new[] { "1234567890", "abcde", "fghij", "klmno" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 5, charBudget: 17);

        // line 1 costs 11 (10 chars + newline); line 2 would add 6 more (17, at budget) so it's
        // included; line 3 would push to 23, over budget, so the page stops before it.
        Assert.AreEqual(3, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageAlwaysIncludesAtLeastOneLineEvenOverBudget()
    {
        var lines = new[] { "a very long line that alone exceeds the budget", "next" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 3, charBudget: 5);

        Assert.AreEqual(2, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageNeverStopsInsideAFencedCodeBlockOrTable()
    {
        // Lines 1-2 are prose; 3-5 are one fenced block; 6 is prose. A budget that would
        // naturally cut inside the block must instead extend through line 5.
        var lines = new[] { "before", "before2", "```", "code line", "```", "after" };
        var atomicBlocks = new[] { new MarkdownBlockRange(3, 6) };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine: 1, endLineExclusiveBound: 7, charBudget: 20);

        Assert.AreEqual(6, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageNeverExtendsPastTheBoundEvenWhenABlockWouldCrossIt()
    {
        var lines = new[] { "```", "code", "```" };
        // A block that (pathologically) extends past the requested bound must not pull the page
        // past that bound; the bound wins.
        var atomicBlocks = new[] { new MarkdownBlockRange(1, 4) };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine: 1, endLineExclusiveBound: 3, charBudget: 1);

        Assert.AreEqual(2, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageReturnsNotPartialWhenTheWholeRangeFits()
    {
        var lines = new[] { "a", "b", "c" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 4, charBudget: 1000);

        Assert.AreEqual(4, endLineExclusive);
        Assert.IsFalse(isPartial);
    }
}
