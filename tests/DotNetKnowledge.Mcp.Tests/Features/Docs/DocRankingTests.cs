using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.Docs;

namespace DotNetKnowledge.Mcp.Tests.Features.Docs;

[TestClass]
public sealed class DocRankingTests
{
    private static readonly SourceProvenance Source =
        new("dotnet/csharplang", "pinned", "0000000000000000000000000000000000000000", DateTimeOffset.UnixEpoch);

    private static readonly string[] ByPathThenRepo =
        ["docs/proposal-a.md", "docs/proposal-b.md", "docs/proposal-c.md"];

    private static readonly string[] ExistingSourcesExpectedOrder =
    [
        "proposals/csharp-9.0/records.md",
        "docs/wiki/Roslyn-Overview.md",
        "meetings/2020/LDM-2020-01-08.md",
    ];

    private static DocLineHit Hit(string path, int line, string text) =>
        new(path, line, text, IsTruncated: false, SectionPath: text, Source);

    private static string[] OrderedPaths(string query, params DocLineHit[] hits) =>
        DocRanking.Order(hits, query).Select(hit => hit.Path).ToArray();

    [TestMethod]
    public void ProposalOutranksMeetingNotes()
    {
        // "collection expressions" should surface the proposal, not the LDM agenda line that names it.
        var ordered = OrderedPaths(
            "collection expressions",
            Hit("meetings/2022/LDM-2022-03-09.md", 5, "1. Ambiguity of collection expressions"),
            Hit("proposals/csharp-12.0/collection-expressions.md", 40, "Collection expressions are..."));

        Assert.AreEqual("proposals/csharp-12.0/collection-expressions.md", ordered[0]);
    }

    [TestMethod]
    public void HeadingMatchOutranksProseMatchInSameDocument()
    {
        var ordered = OrderedPaths(
            "patterns",
            Hit("proposals/patterns.md", 30, "See the section on patterns below."),
            Hit("proposals/patterns.md", 90, "## List patterns"));

        // The heading hit wins despite its later line number.
        Assert.AreEqual(90, DocRanking.Order(
            [
                Hit("proposals/patterns.md", 30, "See the section on patterns below."),
                Hit("proposals/patterns.md", 90, "## List patterns"),
            ],
            "patterns")[0].Line);
        Assert.HasCount(2, ordered);
    }

    [TestMethod]
    public void EqualWeightKeepsPathThenRepoOrdinalOrder()
    {
        var ordered = OrderedPaths(
            "feature",
            Hit("docs/proposal-c.md", 3, "feature C"),
            Hit("docs/proposal-a.md", 3, "feature A"),
            Hit("docs/proposal-b.md", 3, "feature B"));

        CollectionAssert.AreEqual(ByPathThenRepo, ordered);
    }

    [TestMethod]
    public void CurrentNuGetGuidanceOutranksReleaseNotesAndArchive()
    {
        var ordered = OrderedPaths(
            "restore",
            Hit("docs/archive/NuGet-2.x-release-notes.md", 88, "restore behavior changed"),
            Hit("docs/release-notes/NuGet-6.0.md", 14, "restore behavior changed"),
            Hit("docs/consume-packages/Package-Restore.md", 30, "restore behavior changed"));

        Assert.AreEqual("docs/consume-packages/Package-Restore.md", ordered[0]);
    }

    [TestMethod]
    public void LanguageProposalsOutrankNuGetGuidance()
    {
        // Equal tiers fall through to the path tiebreak, where "docs/" sorts before "proposals/".
        // Tiering NuGet below proposals is what keeps an unfiltered language query answering with
        // language documents.
        var ordered = OrderedPaths(
            "records",
            Hit("docs/reference/nuspec.md", 10, "records are listed here"),
            Hit("proposals/csharp-9.0/records.md", 3, "records are declared like this"));

        Assert.AreEqual("proposals/csharp-9.0/records.md", ordered[0]);
    }

    [TestMethod]
    public void ExistingSourcesKeepTheirRelativeOrder()
    {
        // The change inserts a tier and splits "historical" out of the middle. It must not reorder
        // anything that already existed: proposal, then wiki, then meeting notes.
        var ordered = OrderedPaths(
            "records",
            Hit("meetings/2020/LDM-2020-01-08.md", 5, "records discussion"),
            Hit("docs/wiki/Roslyn-Overview.md", 12, "records overview"),
            Hit("proposals/csharp-9.0/records.md", 3, "records proposal"));

        CollectionAssert.AreEqual(ExistingSourcesExpectedOrder, ordered);
    }
}
