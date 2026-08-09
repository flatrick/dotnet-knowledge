using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Tests.Text;

[TestClass]
public sealed class CallerInputNormalizationTests
{
    [TestMethod]
    [DataRow("Filter with x &gt; y", "Filter with x > y")]
    [DataRow("Span&lt;char&gt; support", "Span<char> support")]
    [DataRow("Tom &amp; Jerry", "Tom & Jerry")]
    [DataRow("&quot;quoted&quot;", "\"quoted\"")]
    [DataRow("It&#39;s", "It's")]
    public void TryNormalizeDecodesHtmlEntities(string input, string expected)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual(expected, normalized);
    }

    [TestMethod]
    [DataRow("\u2018quoted\u2019", "'quoted'")]
    [DataRow("\u201Cquoted\u201D", "\"quoted\"")]
    public void TryNormalizeFoldsCurlyQuotesToStraightQuotes(string input, string expected)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual(expected, normalized);
    }

    [TestMethod]
    public void TryNormalizeFoldsNonBreakingSpaceToRegularSpace()
    {
        var changed = CallerInputNormalization.TryNormalize("a\u00A0b", out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual("a b", normalized);
    }

    [TestMethod]
    [DataRow("Feature A > Motivation")]
    [DataRow("plain text with no artifacts")]
    [DataRow("")]
    public void TryNormalizeReturnsFalseWhenInputIsAlreadyClean(string input)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsFalse(changed);
        Assert.AreEqual(input, normalized);
    }
}