using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiSearchRankingTests
{
    private static readonly SourceProvenance Source =
        new("test/dotnet-api-docs", "pinned", "0000000000000000000000000000000000000000", DateTimeOffset.UnixEpoch);

    private static readonly string[] PrefixBeforeSubstring = ["System.SpanExtensions", "System.ClassifiedSpan"];
    private static readonly string[] OrdinalWidgets =
        ["System.AlphaWidget", "System.BetaWidget", "System.GammaWidget"];
    private static readonly string[] NamespaceByDepthThenOrdinal =
        ["System.Widget", "System.WidgetKit", "System.Widgets.Gadget"];

    private static ApiSearchItem Type(string name) => new(name, ApiNameMatch.Type, null, Source);

    private static ApiSearchItem Namespace(string name, int depth) =>
        new(name, ApiNameMatch.Namespace, depth, Source);

    private static ApiSearchItem FullName(string name) => new(name, ApiNameMatch.FullName, null, Source);

    private static string[] OrderedNames(string pattern, params ApiSearchItem[] items) =>
        ApiSearchRanking.Order(items, pattern).Select(item => item.Name).ToArray();

    [TestMethod]
    public void ExactTypeNameRanksAboveSubstring()
    {
        // System.Span<T> is the answer to "Span"; the Roslyn *Span* types are noise. An exact
        // simple-name match must beat a mere substring match.
        var ordered = OrderedNames(
            "Span",
            Type("Microsoft.CodeAnalysis.Classification.ClassifiedSpan"),
            Type("System.Span`1"),
            Type("Microsoft.CodeAnalysis.CSharp.Syntax.LineOrSpanDirectiveTriviaSyntax"));

        Assert.AreEqual("System.Span`1", ordered[0]);
    }

    [TestMethod]
    public void PrefixRanksAboveNonPrefixSubstring()
    {
        var ordered = OrderedNames(
            "Span",
            Type("System.ClassifiedSpan"),
            Type("System.SpanExtensions"));

        CollectionAssert.AreEqual(PrefixBeforeSubstring, ordered);
    }

    [TestMethod]
    public void EqualMatchesKeepOrdinalOrder()
    {
        // Guards the deterministic-paging contract: among equally-ranked substring matches the
        // order is the ordinal one, so an offset cursor keeps addressing the same result set.
        var ordered = OrderedNames(
            "Widget",
            Type("System.GammaWidget"),
            Type("System.AlphaWidget"),
            Type("System.BetaWidget"));

        CollectionAssert.AreEqual(OrdinalWidgets, ordered);
    }

    [TestMethod]
    public void ShallowNamespaceRanksAboveDeeplyNested()
    {
        var ordered = OrderedNames(
            "Token",
            Type("Microsoft.CodeAnalysis.CSharp.Syntax.SyntaxTokenList"),
            Type("System.Threading.CancellationToken"));

        Assert.AreEqual("System.Threading.CancellationToken", ordered[0]);
    }

    [TestMethod]
    public void FullNameRanksAboveType()
    {
        var ordered = OrderedNames(
            "Text.Json.JsonSerializer",
            Type("System.Text.Json.Nodes.JsonSerializerNode"),
            FullName("System.Text.Json.JsonSerializer"));

        Assert.AreEqual("System.Text.Json.JsonSerializer", ordered[0]);
    }

    [TestMethod]
    public void NamespaceMatchesOrderedByDepthThenOrdinal()
    {
        var ordered = OrderedNames(
            "System",
            Namespace("System.Widgets.Gadget", 1),
            Namespace("System.WidgetKit", 0),
            Namespace("System.Widget", 0));

        CollectionAssert.AreEqual(NamespaceByDepthThenOrdinal, ordered);
    }

    [TestMethod]
    public void TypeMatchesRankAboveNamespaceMatches()
    {
        var ordered = OrderedNames(
            "Json",
            Namespace("System.Text.Json.JsonDocument", 0),
            Type("System.Text.JsonName"));

        Assert.AreEqual("System.Text.JsonName", ordered[0]);
    }
}
