using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiSearchRankingTests
{
    private static readonly GitProvenance Source =
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
    public void ApiProvenanceUsesDiscriminatedWireShapesWithoutRevisionKey()
    {
        var assembly = typeof(ApiDocsQueryService).Assembly;
        var baseType = assembly.GetType("DotNetKnowledge.Mcp.Features.ApiDocs.ApiProvenance");
        var gitType = assembly.GetType("DotNetKnowledge.Mcp.Features.ApiDocs.GitProvenance");
        var nugetType = assembly.GetType("DotNetKnowledge.Mcp.Features.ApiDocs.NuGetProvenance");

        Assert.IsNotNull(baseType, "API provenance must have a common discriminated base type.");
        Assert.IsNotNull(gitType, "Git API provenance must be a distinct wire shape.");
        Assert.IsNotNull(nugetType, "NuGet API provenance must be a distinct wire shape.");

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var git = Activator.CreateInstance(
            gitType,
            "test/dotnet-api-docs",
            "pinned",
            "0000000000000000000000000000000000000000",
            DateTimeOffset.UnixEpoch);
        using var gitDocument = JsonDocument.Parse(JsonSerializer.Serialize(git, baseType, options));
        Assert.AreEqual("git", gitDocument.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("test/dotnet-api-docs", gitDocument.RootElement.GetProperty("repo").GetString());
        Assert.AreEqual("pinned", gitDocument.RootElement.GetProperty("ref").GetString());
        Assert.IsTrue(gitDocument.RootElement.TryGetProperty("commit", out _));
        Assert.IsFalse(gitDocument.RootElement.TryGetProperty("revisionKey", out _));

        var nuget = Activator.CreateInstance(
            nugetType,
            "Microsoft.CodeAnalysis.Workspaces.MSBuild",
            "5.3.0",
            "verified-sha512",
            "https://api.nuget.org/v3/index.json",
            "net10.0",
            DateTimeOffset.UnixEpoch);
        using var nugetDocument = JsonDocument.Parse(JsonSerializer.Serialize(nuget, baseType, options));
        Assert.AreEqual("nuget", nugetDocument.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("Microsoft.CodeAnalysis.Workspaces.MSBuild", nugetDocument.RootElement.GetProperty("packageId").GetString());
        Assert.AreEqual("5.3.0", nugetDocument.RootElement.GetProperty("version").GetString());
        Assert.AreEqual("verified-sha512", nugetDocument.RootElement.GetProperty("sha512").GetString());
        Assert.AreEqual("https://api.nuget.org/v3/index.json", nugetDocument.RootElement.GetProperty("feed").GetString());
        Assert.AreEqual("net10.0", nugetDocument.RootElement.GetProperty("framework").GetString());
        Assert.IsTrue(nugetDocument.RootElement.TryGetProperty("fetchedAt", out _));
        Assert.IsFalse(nugetDocument.RootElement.TryGetProperty("repo", out _));
        Assert.IsFalse(nugetDocument.RootElement.TryGetProperty("commit", out _));
        Assert.IsFalse(nugetDocument.RootElement.TryGetProperty("revisionKey", out _));

        var restored = JsonSerializer.Deserialize(
            nugetDocument.RootElement.GetRawText(),
            baseType,
            options);
        Assert.IsNotNull(restored);
        Assert.AreEqual(nugetType, restored.GetType());

        var delimitedGit = Activator.CreateInstance(
            gitType,
            "one@two",
            "three",
            "four",
            DateTimeOffset.UnixEpoch);
        var adjacentGit = Activator.CreateInstance(
            gitType,
            "one",
            "two@three",
            "four",
            DateTimeOffset.UnixEpoch);
        var revisionKey = (string)gitType.GetProperty("RevisionKey")!.GetValue(delimitedGit)!;
        var adjacentRevisionKey = (string)gitType.GetProperty("RevisionKey")!.GetValue(adjacentGit)!;
        Assert.AreNotEqual(revisionKey, adjacentRevisionKey);
    }

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
    public void GenericTypeCountsAsExactNameOverNonGenericNamesake()
    {
        // System.Span`1 is what "Span" almost always means; its `1 arity must not demote it below a
        // non-generic namesake sitting in a deeper namespace.
        var ordered = OrderedNames(
            "Span",
            Type("System.Windows.Documents.Span"),
            Type("System.Span`1"));

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
