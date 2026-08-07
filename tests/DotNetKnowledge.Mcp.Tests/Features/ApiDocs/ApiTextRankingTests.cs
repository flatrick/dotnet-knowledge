using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiTextRankingTests
{
    private static readonly SourceProvenance Source =
        new("test/dotnet-api-docs", "pinned", "0000000000000000000000000000000000000000", DateTimeOffset.UnixEpoch);

    private static readonly string[] SummaryThenRemarks =
        ["System.Math.Round", "System.Decimal.Round"];
    private static readonly string[] OrdinalWithinElement =
        ["System.Alpha", "System.Beta", "System.Gamma"];

    private static ApiTextHit Hit(string symbol, string element, string text) =>
        new(symbol, element, text, IsTruncated: false, Source);

    private static string[] OrderedSymbols(string query, params ApiTextHit[] hits) =>
        ApiTextRanking.Order(hits, query).Select(hit => hit.Symbol).ToArray();

    [TestMethod]
    public void SummaryElementRanksAboveRemarks()
    {
        var ordered = OrderedSymbols(
            "midpoint",
            Hit("System.Decimal.Round", "remarks", "those strategies apply for midpoint values"),
            Hit("System.Math.Round", "summary", "rounds midpoint values to the nearest even number"));

        CollectionAssert.AreEqual(SummaryThenRemarks, ordered);
    }

    [TestMethod]
    public void WholeWordMatchRanksAboveMidWordSubstring()
    {
        // "span" buried inside "expands" is a weaker hit than "span" standing as its own word.
        var ordered = OrderedSymbols(
            "span",
            Hit("System.Expander", "summary", "expands the buffer in place"),
            Hit("System.Span", "summary", "a span over contiguous memory"));

        Assert.AreEqual("System.Span", ordered[0]);
    }

    [TestMethod]
    public void EqualRankKeepsOrdinalOrder()
    {
        var ordered = OrderedSymbols(
            "widget",
            Hit("System.Gamma", "summary", "a widget"),
            Hit("System.Alpha", "summary", "a widget"),
            Hit("System.Beta", "summary", "a widget"));

        CollectionAssert.AreEqual(OrdinalWithinElement, ordered);
    }

    [TestMethod]
    public void CollapsePerSymbolKeepsTopHitsAndCountsTheRest()
    {
        // One symbol with four matching doc elements would consume most of a page. Keep the two
        // best and record how many more the caller can reach with lookup_api.
        var collapsed = ApiTextRanking.CollapsePerSymbol(
            [
                Hit("System.Math.Round", "summary", "rounds midpoint values"),
                Hit("System.Math.Round", "remarks", "midpoint values, that is, values ending in 5"),
                Hit("System.Math.Round", "remarks", "another midpoint remark"),
                Hit("System.Math.Round", "exception", "a midpoint related exception"),
                Hit("System.Decimal.Round", "summary", "rounds midpoint values for decimal"),
            ],
            perSymbolLimit: 2);

        var round = collapsed.Where(hit => hit.Symbol == "System.Math.Round").ToArray();
        Assert.HasCount(2, round);
        Assert.IsNull(round[0].MoreFromSymbol);
        Assert.AreEqual(2, round[1].MoreFromSymbol);

        var decimalRound = collapsed.Single(hit => hit.Symbol == "System.Decimal.Round");
        Assert.IsNull(decimalRound.MoreFromSymbol);
    }
}
