using System.Text.RegularExpressions;
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownLineSearchTests
{
    // 1: # Title      4: Some prose in A.
    // 2:               5:
    // 3: ## A          6: class Foo in a code line
    private const string Document = "# Title\n\n## A\n\nSome prose in A.\n\nclass Foo in a code line\n";

    [TestMethod]
    public void SearchLiteralMatchesCaseSensitiveSubstringsAndAttributesTheEnclosingSection()
    {
        var outline = MarkdownOutline.Extract(Document);

        var hits = MarkdownLineSearch.Search(Document, outline, "prose", regex: false);

        Assert.HasCount(1, hits);
        Assert.AreEqual(5, hits[0].Line);
        Assert.AreEqual("Title > A", hits[0].SectionPath);

        Assert.IsEmpty(MarkdownLineSearch.Search(Document, outline, "PROSE", regex: false));
    }

    [TestMethod]
    public void SearchRegexMatchesAndAttributesTheEnclosingSection()
    {
        var outline = MarkdownOutline.Extract(Document);

        var hits = MarkdownLineSearch.Search(Document, outline, "class \\w+", regex: true);

        Assert.HasCount(1, hits);
        Assert.AreEqual(7, hits[0].Line);
        Assert.AreEqual("Title > A", hits[0].SectionPath);
    }

    [TestMethod]
    public void SearchRegexThrowsBeforeMatchingWhenThePatternUsesABackreference()
    {
        var outline = MarkdownOutline.Extract(Document);

        Assert.ThrowsExactly<NotSupportedException>(() =>
            MarkdownLineSearch.Search(Document, outline, @"(\w+)\s+\1", regex: true));
    }

    [TestMethod]
    public void SearchRegexThrowsAParseExceptionForInvalidSyntax()
    {
        var outline = MarkdownOutline.Extract(Document);

        Assert.ThrowsExactly<RegexParseException>(() =>
            MarkdownLineSearch.Search(Document, outline, "[unterminated(", regex: true));
    }
}
