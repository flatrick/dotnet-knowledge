using System.Text.RegularExpressions;
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownLineSearchTests
{
    // 1: # Title      5: Some prose in A.
    // 2:               6:
    // 3: ## A          7: class Foo in a code line
    // 4:
    private const string Document = "# Title\n\n## A\n\nSome prose in A.\n\nclass Foo in a code line\n";

    // 1: ---            5: ---
    // 2: title: ...     6:
    // 3: ms.author: ... 7: # Heading One
    // 4: ms.date: ...   8:
    //                   9: Body text about the title of a package.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "---\n" +
        "\n" +
        "# Heading One\n" +
        "\n" +
        "Body text about the title of a package.\n";

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

    [TestMethod]
    public void SearchIgnoresFrontMatterAndStillMatchesTheBody()
    {
        var outline = MarkdownOutline.Extract(LearnArticle);

        // "title" appears on line 2 as a metadata key and on line 9 as prose. Only the prose is a
        // documentation hit; the key is metadata about the document, not part of it.
        var hits = MarkdownLineSearch.Search(LearnArticle, outline, "title", regex: false);

        Assert.HasCount(1, hits);
        Assert.AreEqual(9, hits[0].Line);
        Assert.AreEqual("Heading One", hits[0].SectionPath);
    }

    [TestMethod]
    public void SearchReturnsNothingForAFrontMatterOnlyTerm()
    {
        var outline = MarkdownOutline.Extract(LearnArticle);

        Assert.IsEmpty(MarkdownLineSearch.Search(LearnArticle, outline, "ms.author", regex: false));
    }
}
