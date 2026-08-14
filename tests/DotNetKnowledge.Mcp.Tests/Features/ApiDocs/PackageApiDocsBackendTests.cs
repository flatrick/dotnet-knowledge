using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class PackageApiDocsBackendTests
{
    private static readonly string[] ExpectedMemberNames = ["Convert<TResult>", "Create"];
    private static readonly string[] ExpectedResolvedTypes = ["System.Widget"];
    private static readonly string[] ExpectedFrameworks = ["net10.0", "net8.0"];
    private static readonly string[] SkippedPropertyNames = ["Alpha", "Beta", "Gamma"];

    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-package-backend-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            ApiDocsFixture.DeleteDirectory(_root);
    }

    [TestMethod]
    public void SummarizeSkippedReportsNothingForACompleteCorpus() =>
        Assert.IsNull(PackageApiDocsBackend.SummarizeSkipped([]));

    [TestMethod]
    public void SummarizeSkippedGroupsOneDefectAcrossManyDeclarations()
    {
        // The reader interpolates the declaration name into its message, so grouping on the raw
        // text would report one reason per skip -- the list the summary exists to replace.
        var skipped = SkippedPropertyNames
            .Select(name => new ApiSkippedDeclaration(
                "property",
                "Fixture.Type",
                name,
                $"The accessors for property '{name}' have incompatible modifiers."))
            .ToArray();

        var summary = PackageApiDocsBackend.SummarizeSkipped(skipped);

        Assert.IsNotNull(summary);
        Assert.AreEqual(3, summary.Declarations);
        Assert.AreEqual(1, summary.Reasons.Count);
        Assert.AreEqual(3, summary.Reasons[0].Count);
        Assert.IsFalse(summary.ReasonsArePartial);
        StringAssert.Contains(summary.Reasons[0].Reason, "incompatible modifiers");
    }

    [TestMethod]
    public void SummarizeSkippedMarksACappedReasonListPartial()
    {
        var skipped = Enumerable.Range(0, 15)
            .Select(index => new ApiSkippedDeclaration(
                "method", "Fixture.Type", $"M{index}", $"Distinct reason {index}."))
            .ToArray();

        var summary = PackageApiDocsBackend.SummarizeSkipped(skipped);

        Assert.IsNotNull(summary);
        Assert.AreEqual(15, summary.Declarations, "The total must survive the reason cap.");
        Assert.AreEqual(10, summary.Reasons.Count);
        Assert.IsTrue(summary.ReasonsArePartial);
    }

    [TestMethod]
    public async Task LookupReturnsExactTypeAndGenericMemberMatchesWithoutPaging()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var type = backend.Lookup("System.Widget", CancellationToken.None);
        var member = backend.Lookup("System.Widget.Convert", CancellationToken.None);
        var missing = backend.Lookup("System.Widget.Missing", CancellationToken.None);

        Assert.HasCount(1, type.Matches);
        Assert.AreEqual(ApiLookupDetail.Signatures, type.Matches[0].Detail);
        CollectionAssert.AreEqual(
            ExpectedMemberNames,
            type.Matches[0].Members.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal).ToArray());
        Assert.HasCount(1, member.Matches);
        Assert.AreEqual(ApiLookupDetail.Full, member.Matches[0].Detail);
        Assert.AreEqual("Convert<TResult>", member.Matches[0].Members.Single().Name);
        Assert.HasCount(0, missing.Matches);
        CollectionAssert.AreEqual(ExpectedResolvedTypes, missing.ResolvedTypeNames.ToArray());
    }

    [TestMethod]
    public async Task RepositoryAndPackageReadsCarryEquivalentCanonicalDeclarationIds()
    {
        var package = await ApiDocsFixture.CreatePackageBackendAsync(Path.Combine(_root, "package"));
        var repository = await ApiDocsFixture.CreateWidgetRepositoryBackendAsync(Path.Combine(_root, "repository"));

        var packageLookup = package.Lookup("System.Widget.Create", CancellationToken.None);
        var repositoryLookup = repository.Lookup("System.Widget.Create", CancellationToken.None);
        var repositoryOverloads = repository.Lookup("System.Widget.Convert", CancellationToken.None);
        var repositoryOverloadText = repository.SearchText("Converts", CancellationToken.None);
        var packageText = package.SearchText("Creates a widget", CancellationToken.None);
        var repositoryText = repository.SearchText("Creates a widget", CancellationToken.None);
        var packageReferences = package.FindReferences("System.String", CancellationToken.None);
        var repositoryReferences = repository.FindReferences("System.String", CancellationToken.None);

        Assert.AreEqual("T:System.Widget", packageLookup.Matches.Single().DeclarationId);
        Assert.AreEqual(
            packageLookup.Matches.Single().DeclarationId,
            repositoryLookup.Matches.Single().DeclarationId);
        Assert.AreEqual(
            "M:System.Widget.Create(System.String)",
            packageLookup.Matches.Single().Members.Single().DeclarationId);
        Assert.AreEqual(
            packageLookup.Matches.Single().Members.Single().DeclarationId,
            repositoryLookup.Matches.Single().Members.Single().DeclarationId);
        Assert.HasCount(2, repositoryOverloads.Matches.Single().Members);
        Assert.HasCount(
            2,
            repositoryOverloads.Matches.Single().Members
                .Select(member => member.DeclarationId)
                .Distinct(StringComparer.Ordinal));
        Assert.HasCount(2, repositoryOverloadText.Hits);
        Assert.HasCount(
            2,
            repositoryOverloadText.Hits
                .Select(hit => hit.DeclarationId)
                .Distinct(StringComparer.Ordinal));
        Assert.AreEqual(
            packageLookup.Matches.Single().Members.Single().DeclarationId,
            packageText.Hits.Single().DeclarationId);
        Assert.AreEqual(
            packageText.Hits.Single().DeclarationId,
            repositoryText.Hits.Single().DeclarationId);
        Assert.AreEqual(
            packageLookup.Matches.Single().Members.Single().DeclarationId,
            packageReferences.Items.Single(item => item.Hit.Symbol == "System.Widget.Create").DeclarationId);
        Assert.AreEqual(
            packageReferences.Items.Single(item => item.Hit.Symbol == "System.Widget.Create").DeclarationId,
            repositoryReferences.Items.Single(item => item.Hit.Symbol == "System.Widget.Create").DeclarationId);
    }

    [TestMethod]
    public async Task RepositoryNamespaceTextUsesCanonicalNamespaceIdentity()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "namespace"),
            [("xml/ns-System.Example.xml", """
                <Namespace Name="System.Example">
                  <Docs><summary>Namespace identity needle.</summary></Docs>
                </Namespace>
                """)]);

        var hit = backend.SearchText("identity needle", CancellationToken.None).Hits.Single();

        Assert.AreEqual("N:System.Example", hit.DeclarationId);
        Assert.AreEqual("System.Example", hit.Symbol);
    }

    [TestMethod]
    public async Task RepositoryTextIgnoresNonDeclarationXmlIndexes()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "index-root"),
            [
                ("xml/index.xml", "<Overview><Docs><summary>Index-only needle.</summary></Docs></Overview>"),
                ("xml/System/Valid.xml", """
                    <Type Name="Valid" FullName="System.Valid">
                      <TypeSignature Language="DocId" Value="T:System.Valid" />
                      <Docs><summary>Declaration needle.</summary></Docs>
                    </Type>
                    """),
            ]);

        var hits = backend.SearchText("needle", CancellationToken.None).Hits;

        Assert.HasCount(1, hits);
        Assert.AreEqual("T:System.Valid", hits.Single().DeclarationId);
    }

    [TestMethod]
    public async Task RepositoryLookupRejectsMissingAndMalformedCanonicalDocIds()
    {
        var missing = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "missing-id"),
            [("xml/System/Broken.xml", """
                <Type Name="Broken" FullName="System.Broken">
                  <TypeSignature Language="C#" Value="public class Broken" />
                </Type>
                """)]);
        var malformed = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "malformed-id"),
            [("xml/System/Broken.xml", """
                <Type Name="Broken" FullName="System.Broken">
                  <TypeSignature Language="DocId" Value="M:System.Broken" />
                </Type>
                """)]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            missing.Lookup("System.Broken", CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            malformed.Lookup("System.Broken", CancellationToken.None));
    }

    [TestMethod]
    public async Task RepositoryLookupRejectsMissingMalformedAndDuplicateMemberDocIds()
    {
        static string TypeWithMembers(string members) => $$"""
            <Type Name="Broken" FullName="System.Broken">
              <TypeSignature Language="DocId" Value="T:System.Broken" />
              <Members>{{members}}</Members>
            </Type>
            """;
        static string Member(string name, string docId) => $$"""
            <Member MemberName="{{name}}">
              <MemberSignature Language="C#" Value="public void {{name}}();" />
              {{docId}}
            </Member>
            """;

        var cases = new[]
        {
            TypeWithMembers(Member("Missing", "")),
            TypeWithMembers(Member("Malformed", "<MemberSignature Language=\"DocId\" Value=\"T:System.Broken.Malformed\" />")),
            TypeWithMembers(
                Member("First", "<MemberSignature Language=\"DocId\" Value=\"M:System.Broken.Shared\" />")
                + Member("Second", "<MemberSignature Language=\"DocId\" Value=\"M:System.Broken.Shared\" />")),
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
                Path.Combine(_root, "member-id-" + index),
                [("xml/System/Broken.xml", cases[index])]);
            Assert.ThrowsExactly<InvalidDataException>(() =>
                backend.Lookup("System.Broken", CancellationToken.None));
        }
    }

    [TestMethod]
    public async Task RepositoryConversionUsesTheReturnQualifiedCanonicalDocIdVariant()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "conversion-id"),
            [("xml/System/Convertible.xml", """
                <Type Name="Convertible" FullName="System.Convertible">
                  <TypeSignature Language="DocId" Value="T:System.Convertible" />
                  <Members>
                    <Member MemberName="op_Implicit">
                      <MemberSignature Language="C#" Value="public static implicit operator int(System.Convertible value);" />
                      <MemberSignature Language="DocId" Value="M:System.Convertible.op_Implicit(System.Convertible)" />
                      <MemberSignature Language="DocId" Value="M:System.Convertible.op_Implicit(System.Convertible)~System.Int32" FrameworkAlternate="old" />
                    </Member>
                  </Members>
                </Type>
                """)]);

        var member = backend.Lookup("System.Convertible.op_Implicit", CancellationToken.None)
            .Matches.Single().Members.Single();

        Assert.AreEqual(
            "M:System.Convertible.op_Implicit(System.Convertible)~System.Int32",
            member.DeclarationId);
    }

    [TestMethod]
    public async Task RepositoryCheckedConversionUsesTheReturnQualifiedCanonicalDocIdVariant()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "checked-conversion-id"),
            [("xml/System/Convertible.xml", """
                <Type Name="Convertible" FullName="System.Convertible">
                  <TypeSignature Language="DocId" Value="T:System.Convertible" />
                  <Members>
                    <Member MemberName="op_CheckedExplicit">
                      <MemberSignature Language="C#" Value="public static explicit operator checked int(System.Convertible value);" />
                      <MemberSignature Language="DocId" Value="M:System.Convertible.op_CheckedExplicit(System.Convertible)" />
                      <MemberSignature Language="DocId" Value="M:System.Convertible.op_CheckedExplicit(System.Convertible)~System.Int32" FrameworkAlternate="old" />
                    </Member>
                  </Members>
                </Type>
                """)]);

        var member = backend.Lookup("System.Convertible.op_CheckedExplicit", CancellationToken.None)
            .Matches.Single().Members.Single();

        Assert.AreEqual(
            "M:System.Convertible.op_CheckedExplicit(System.Convertible)~System.Int32",
            member.DeclarationId);
    }

    [TestMethod]
    public async Task RepositoryMemberRejectsAmbiguousUnqualifiedDocIdsIndependentOfXmlOrder()
    {
        var signatures = new[]
        {
            "<MemberSignature Language=\"DocId\" Value=\"M:System.Ambiguous.First\" />",
            "<MemberSignature Language=\"DocId\" Value=\"M:System.Ambiguous.Second\" />",
        };
        for (var index = 0; index < 2; index++)
        {
            var ordered = index == 0 ? signatures : signatures.Reverse().ToArray();
            var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
                Path.Combine(_root, "ambiguous-member-" + index),
                [("xml/System/Ambiguous.xml", $$"""
                    <Type Name="Ambiguous" FullName="System.Ambiguous">
                      <TypeSignature Language="DocId" Value="T:System.Ambiguous" />
                      <Members><Member MemberName="Run">
                        <MemberSignature Language="C#" Value="public void Run();" />
                        {{string.Join(Environment.NewLine, ordered)}}
                      </Member></Members>
                    </Type>
                    """)]);

            Assert.ThrowsExactly<InvalidDataException>(() =>
                backend.Lookup("System.Ambiguous.Run", CancellationToken.None));
        }
    }

    [TestMethod]
    public async Task RepositoryMemberCollapsesIdenticalDocIdAlternates()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "identical-member-alternates"),
            [("xml/System/Stable.xml", """
                <Type Name="Stable" FullName="System.Stable">
                  <TypeSignature Language="DocId" Value="T:System.Stable" />
                  <Members><Member MemberName="Run">
                    <MemberSignature Language="C#" Value="public void Run();" />
                    <MemberSignature Language="DocId" Value="M:System.Stable.Run" />
                    <MemberSignature Language="DocId" Value="M:System.Stable.Run" FrameworkAlternate="old" />
                  </Member></Members>
                </Type>
                """)]);

        var member = backend.Lookup("System.Stable.Run", CancellationToken.None)
            .Matches.Single().Members.Single();

        Assert.AreEqual("M:System.Stable.Run", member.DeclarationId);
    }

    [TestMethod]
    public async Task RepositoryRejectsControlCharactersInCanonicalDeclarationIds()
    {
        var type = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "control-type-id"),
            [("xml/System/Bad.xml", """
                <Type Name="Bad" FullName="System.Bad">
                  <TypeSignature Language="DocId" Value="T:System.Bad&#x7F;Type" />
                </Type>
                """)]);
        var member = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "control-member-id"),
            [("xml/System/Bad.xml", """
                <Type Name="Bad" FullName="System.Bad">
                  <TypeSignature Language="DocId" Value="T:System.Bad" />
                  <Members><Member MemberName="Run">
                    <MemberSignature Language="C#" Value="public void Run();" />
                    <MemberSignature Language="DocId" Value="M:System.Bad.Run&#x7F;" />
                  </Member></Members>
                </Type>
                """)]);
        var namespaced = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "control-namespace-id"),
            [("xml/ns-System.Bad.xml", """
                <Namespace Name="System.Bad&#x7F;Namespace">
                  <Docs><summary>Control identity needle.</summary></Docs>
                </Namespace>
                """)]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            type.Lookup("System.Bad", CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            member.Lookup("System.Bad.Run", CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            namespaced.SearchText("Control identity needle", CancellationToken.None));
    }

    [TestMethod]
    public async Task RepositoryLookupRejectsDuplicateTypeDeclarationIds()
    {
        var backend = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "duplicate-type-id"),
            [
                ("xml/System/Holder.xml", """
                    <Type Name="Holder" FullName="System.Holder">
                      <TypeSignature Language="DocId" Value="T:System.Holder" />
                    </Type>
                    """),
                ("xml/System/Holder`1.xml", """
                    <Type Name="Holder&lt;T&gt;" FullName="System.Holder&lt;T&gt;">
                      <TypeSignature Language="DocId" Value="T:System.Holder" />
                    </Type>
                    """),
            ]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            backend.Lookup("System.Holder", CancellationToken.None));
    }

    [TestMethod]
    public async Task RepositoryTextReadRejectsDuplicateNamespaceAndMemberDeclarationIds()
    {
        var duplicateNamespaces = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "duplicate-namespaces"),
            [
                ("xml/ns-System.Example.xml", """
                    <Namespace Name="System.Example">
                      <Docs><summary>Duplicate identity needle.</summary></Docs>
                    </Namespace>
                    """),
                ("xml/Other/ns-System.Example.xml", """
                    <Namespace Name="System.Example">
                      <Docs><summary>Duplicate identity needle.</summary></Docs>
                    </Namespace>
                    """),
            ]);
        var duplicateMembers = await ApiDocsFixture.CreateRepositoryBackendAsync(
            Path.Combine(_root, "duplicate-members-text"),
            [("xml/System/Broken.xml", """
                <Type Name="Broken" FullName="System.Broken">
                  <TypeSignature Language="DocId" Value="T:System.Broken" />
                  <Members>
                    <Member MemberName="First">
                      <MemberSignature Language="C#" Value="public void First();" />
                      <MemberSignature Language="DocId" Value="M:System.Broken.Shared" />
                      <Docs><summary>Duplicate identity needle.</summary></Docs>
                    </Member>
                    <Member MemberName="Second">
                      <MemberSignature Language="C#" Value="public void Second();" />
                      <MemberSignature Language="DocId" Value="M:System.Broken.Shared" />
                      <Docs><summary>Duplicate identity needle.</summary></Docs>
                    </Member>
                  </Members>
                </Type>
                """)]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            duplicateNamespaces.SearchText("identity needle", CancellationToken.None));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            duplicateMembers.SearchText("identity needle", CancellationToken.None));
    }

    [TestMethod]
    public async Task SearchClassifiesTypeNamespaceAndFullNameMatchesForTheSelectedCorpus()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var widget = ApiSearchRanking.Order(backend.Search("Widget", CancellationToken.None).Items, "Widget");
        var system = ApiSearchRanking.Order(backend.Search("System", CancellationToken.None).Items, "System");
        var fullName = backend.Search("System.Widget", CancellationToken.None).Items;

        Assert.IsTrue(widget.Any(item => item.Name == "System.Widget" && item.MatchedOn == ApiNameMatch.Type));
        Assert.IsTrue(widget.Any(item => item.Name == "System.TraitedWidget" && item.MatchedOn == ApiNameMatch.Type));
        Assert.IsTrue(system.Any(item => item.Name == "System.Widget" && item.MatchedOn == ApiNameMatch.Namespace && item.NamespaceDepth == 0));
        Assert.IsTrue(system.Any(item => item.Name == "System.Widgets.Gadget" && item.MatchedOn == ApiNameMatch.Namespace && item.NamespaceDepth == 1));
        Assert.IsTrue(fullName.Any(item => item.Name == "System.Widget" && item.MatchedOn == ApiNameMatch.FullName));
    }

    [TestMethod]
    public async Task SearchUsesCanonicalGenericNamesWithTheSharedRanking()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var items = ApiSearchRanking.Order(backend.Search("Box", CancellationToken.None).Items, "Box");

        Assert.AreEqual("System.Box`1", items.Single().Name);
        Assert.AreEqual(ApiNameMatch.Type, items.Single().MatchedOn);
    }

    [TestMethod]
    public async Task SearchKeepsGenericContainingTypesInTheNestedTypeName()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var items = backend.Search("Outer", CancellationToken.None).Items;
        var nested = items.Single(item => item.Name == "Fixtures.Outer`1.Inner`1");

        Assert.AreEqual(ApiNameMatch.Type, nested.MatchedOn);
        Assert.IsNull(nested.NamespaceDepth);
    }

    [TestMethod]
    public async Task SearchTextUsesRepositoryLabelsAndBudgetForTypeAndMemberDocumentation()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var hits = ApiTextRanking.Order(
            backend.SearchText("Needle", CancellationToken.None).Hits.Select(hit => hit.Hit),
            "Needle");

        var summary = hits.Single(hit => hit.Symbol == "System.StringDerived" && hit.Element == "summary");
        Assert.IsTrue(summary.IsTruncated);
        Assert.IsLessThanOrEqualTo(300, summary.Text.Length);
        Assert.IsTrue(hits.Any(hit => hit.Element == "typeparam:T"));
        Assert.IsTrue(hits.Any(hit => hit.Element == "value"));
        Assert.IsTrue(hits.Any(hit => hit.Element == "remarks"));
        Assert.IsTrue(hits.Any(hit => hit.Element == "exception"));
        Assert.IsTrue(hits.Any(hit => hit.Symbol == "System.StringDerived.Transform" && hit.Element == "param:values"));
        Assert.IsTrue(hits.Any(hit => hit.Symbol == "System.StringDerived.Transform" && hit.Element == "returns"));
    }

    [TestMethod]
    public async Task FindReferencesMatchesCanonicalContainedNamesAndReturnsStoredExpressionsForAllKinds()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var read = backend.FindReferences("System.String", CancellationToken.None);
        var hits = read.Items.OrderBy(item => item.Kind, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEquivalent(ApiReferenceKind.All, hits.Select(item => item.Kind).Distinct().ToArray());
        Assert.AreEqual(
            "string",
            hits.Single(item =>
                item.Kind == ApiReferenceKind.Base
                && item.Symbol == "System.StringDerived").TypeExpression);
        Assert.AreEqual(
            "System.Collections.Generic.IEnumerable<string>",
            hits.Single(item =>
                item.Kind == ApiReferenceKind.Interface
                && item.Symbol == "System.StringDerived").TypeExpression);
        Assert.AreEqual("string", hits.Single(item => item.Kind == ApiReferenceKind.Constraint).TypeExpression);
        var arrayParameter = hits.Single(item =>
            item.Kind == ApiReferenceKind.Parameter
            && item.Symbol == "System.StringDerived.Transform");
        Assert.AreEqual("string[]", arrayParameter.TypeExpression);
        Assert.AreEqual("ref string", hits.Single(item => item.Kind == ApiReferenceKind.Return).TypeExpression);
        Assert.AreEqual(
            "[System.WidgetMarker(typeof(string))]",
            hits.Single(item => item.Kind == ApiReferenceKind.Attribute).TypeExpression);
        Assert.IsTrue(hits.Single(item => item.Kind == ApiReferenceKind.Constraint).IsExact);
        Assert.IsFalse(arrayParameter.IsExact);
        Assert.IsFalse(hits.Single(item => item.Kind == ApiReferenceKind.Return).IsExact);
        Assert.IsFalse(hits.Single(item => item.Kind == ApiReferenceKind.Attribute).IsExact);
    }

    [TestMethod]
    public async Task FindReferencesUsesCanonicalHierarchyNamesAndReturnsRenderedExpressions()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var uri = backend.FindReferences("System.Uri", CancellationToken.None).Items;
        var nested = backend.FindReferences("Fixtures.Outer.Inner", CancellationToken.None).Items;
        var pointer = backend.FindReferences("System.Int32", CancellationToken.None).Items;

        Assert.AreEqual(
            "Fixtures.NullableBase<(string, System.Uri?)>",
            uri.Single(item => item.Kind == ApiReferenceKind.Base).TypeExpression);
        Assert.AreEqual(
            "Fixtures.IHierarchy<Fixtures.Outer<string>.Inner<System.Uri?>>",
            uri.Single(item => item.Kind == ApiReferenceKind.Interface).TypeExpression);
        Assert.AreEqual(
            "Fixtures.Outer<string>.Inner<System.Uri?>",
            nested.Single(item => item.Kind == ApiReferenceKind.Parameter).TypeExpression);
        Assert.AreEqual(
            "int*",
            pointer.Single(item => item.Kind == ApiReferenceKind.Parameter).TypeExpression);
        Assert.IsFalse(pointer.Single(item => item.Kind == ApiReferenceKind.Parameter).IsExact);
    }

    [TestMethod]
    public async Task DuplicateNormalizedAttributeUsesProduceOneDeterministicallyOrderedHit()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var first = backend.FindReferences("System.String", CancellationToken.None).Items;
        var second = backend.FindReferences("System.String", CancellationToken.None).Items;
        var markerHits = first.Where(item =>
            item.Kind == ApiReferenceKind.Attribute
            && item.Symbol == "System.StringDerived").ToArray();

        Assert.HasCount(1, markerHits);
        CollectionAssert.AreEqual(
            first.Select(ReferenceKey).ToArray(),
            second.Select(ReferenceKey).ToArray());
    }

    [TestMethod]
    public async Task PerturbedCorpusInsertionProducesIdenticalOrderedBackendReads()
    {
        var forward = await ApiDocsFixture.CreatePackageBackendAsync(
            Path.Combine(_root, "forward"), reverseFixtureInsertion: false);
        var reversed = await ApiDocsFixture.CreatePackageBackendAsync(
            Path.Combine(_root, "reversed"), reverseFixtureInsertion: true);

        CollectionAssert.AreEqual(
            forward.Lookup("System.Widget", CancellationToken.None).Matches.Single().Members
                .Select(member => member.DeclarationId).ToArray(),
            reversed.Lookup("System.Widget", CancellationToken.None).Matches.Single().Members
                .Select(member => member.DeclarationId).ToArray());
        CollectionAssert.AreEqual(
            forward.Search("Widget", CancellationToken.None).Items.Select(item => item.Name).ToArray(),
            reversed.Search("Widget", CancellationToken.None).Items.Select(item => item.Name).ToArray());
        CollectionAssert.AreEqual(
            forward.SearchText("Needle", CancellationToken.None).Hits
                .Select(hit => hit.DeclarationId + "\0" + hit.Element).ToArray(),
            reversed.SearchText("Needle", CancellationToken.None).Hits
                .Select(hit => hit.DeclarationId + "\0" + hit.Element).ToArray());
        CollectionAssert.AreEqual(
            forward.FindReferences("System.String", CancellationToken.None).Items
                .Select(ReferenceKey).ToArray(),
            reversed.FindReferences("System.String", CancellationToken.None).Items
                .Select(ReferenceKey).ToArray());
    }

    [TestMethod]
    public async Task FindReferencesResolvesAttributeSiblingCollisionsWithinTheSelectedCorpus()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);

        var classRead = backend.FindReferences("System.WidgetTrait", CancellationToken.None);
        var attributeRead = backend.FindReferences("System.WidgetTraitAttribute", CancellationToken.None);
        var nonColliding = backend.FindReferences("System.WidgetSealAttribute", CancellationToken.None);

        Assert.AreEqual("System.WidgetTraitAttribute", classRead.SiblingType);
        Assert.AreEqual(1, classRead.SiblingApplications);
        Assert.IsFalse(classRead.Items.Any(item => item.Kind == ApiReferenceKind.Attribute));
        Assert.IsTrue(classRead.Items.Any(item => item.Kind == ApiReferenceKind.Parameter));
        Assert.AreEqual("System.WidgetTraitAttribute", attributeRead.Items.Single().AttributeType);
        Assert.IsTrue(attributeRead.Items.Single().IsExact);
        Assert.IsNull(nonColliding.SiblingType);
        Assert.AreEqual("System.WidgetSealAttribute", nonColliding.Items.Single().AttributeType);
    }

    [TestMethod]
    public async Task ReadsReportExactNuGetCoverageAndSelectOnlyTheRequestedFrameworkCorpus()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root, "net8.0");

        var read = backend.Lookup("System.LegacyWidget", CancellationToken.None);

        Assert.HasCount(1, read.Matches);
        var defaultOnlyMiss = backend.Lookup("System.Widget", CancellationToken.None);
        Assert.HasCount(0, defaultOnlyMiss.Matches);
        Assert.HasCount(1, defaultOnlyMiss.Coverage.SearchedSources);
        var selectedCorpusAttributes = backend.FindReferences("System.WidgetTrait", CancellationToken.None);
        Assert.IsNull(selectedCorpusAttributes.SiblingType);
        Assert.AreEqual(0, selectedCorpusAttributes.SiblingApplications);
        var provenance = (NuGetProvenance)read.Coverage.SearchedSources.Single();
        Assert.AreEqual("Fixture.Package", provenance.PackageId);
        Assert.AreEqual("1.2.3", provenance.Version);
        Assert.AreEqual(ApiDocsFixture.PackageDefinition().Sha512, provenance.Sha512);
        Assert.AreEqual("https://feed.test/v3/index.json", provenance.Feed);
        Assert.AreEqual("net8.0", provenance.Framework);
        Assert.AreEqual(new DateTimeOffset(2026, 8, 12, 10, 11, 12, TimeSpan.Zero), provenance.FetchedAt);
        Assert.AreEqual("net8.0", read.Coverage.EffectiveFramework);
        Assert.AreEqual("net10.0", read.Coverage.DefaultFramework);
        CollectionAssert.AreEqual(
            ExpectedFrameworks, read.Coverage.AvailableFrameworks!.ToArray());
    }

    [TestMethod]
    public async Task ConstructorRejectsEscapingCorpusPathsAndMismatchedStoredIdentity()
    {
        _ = await ApiDocsFixture.CreatePackageBackendAsync(_root);
        var definition = ApiDocsFixture.PackageDefinition();

        var escapingSnapshot = ApiDocsFixture.PackageSnapshot(
            _root, definition, ApiDocsFixture.PackageState(".."));
        Assert.ThrowsExactly<InvalidDataException>(() => new PackageApiDocsBackend(
            escapingSnapshot, definition.PackageId, "net10.0"));

        var mismatched = ApiDocsFixture.PackageState() with { Version = "2.0.0" };
        var mismatchedSnapshot = ApiDocsFixture.PackageSnapshot(
            _root, definition, mismatched, sourceRef: "head:main");
        Assert.ThrowsExactly<InvalidDataException>(() => new PackageApiDocsBackend(
            mismatchedSnapshot, definition.PackageId, "net10.0"));
    }

    [TestMethod]
    public async Task PinnedSnapshotsRejectObservedPackageIdentityBeforeReadingAValidObservedCorpus()
    {
        var definition = ApiDocsFixture.PackageDefinition();
        var observed = ApiDocsFixture.PackageState() with
        {
            Version = "2.0.0",
            Sha512 = Convert.ToBase64String(Enumerable.Repeat((byte)0x2b, 64).ToArray()),
        };
        await ApiDocsFixture.WritePackageCorpusAsync(_root, observed, "net10.0");
        var snapshot = ApiDocsFixture.PackageSnapshot(_root, definition, observed, sourceRef: "pinned");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(snapshot, definition.PackageId, "net10.0"));
    }

    [TestMethod]
    public async Task ExactConfiguredHeadSnapshotsUseObservedPackageIdentity()
    {
        var definition = ApiDocsFixture.PackageDefinition();
        var observed = ApiDocsFixture.PackageState() with
        {
            Version = "2.0.0",
            Sha512 = Convert.ToBase64String(Enumerable.Repeat((byte)0x2b, 64).ToArray()),
        };
        await ApiDocsFixture.WritePackageCorpusAsync(_root, observed, "net10.0");
        var snapshot = ApiDocsFixture.PackageSnapshot(_root, definition, observed, sourceRef: "head:main");

        var backend = new PackageApiDocsBackend(snapshot, definition.PackageId, "net10.0");
        var provenance = (NuGetProvenance)backend.Lookup("System.Widget", CancellationToken.None)
            .Coverage.SearchedSources.Single();

        Assert.AreEqual(observed.Version, provenance.Version);
        Assert.AreEqual(observed.Sha512, provenance.Sha512);
    }

    [TestMethod]
    public async Task ProductionSourceStateSchemaConstructsAndWrongSchemaFailsBeforeCorpusIo()
    {
        _ = await ApiDocsFixture.CreatePackageBackendAsync(_root);
        var definition = ApiDocsFixture.PackageDefinition();
        var productionSnapshot = ApiDocsFixture.PackageSnapshot(_root, definition);

        var backend = new PackageApiDocsBackend(
            productionSnapshot, definition.PackageId, "net10.0");
        Assert.HasCount(1, backend.Lookup("System.Widget", CancellationToken.None).Matches);

        var wrongSchema = ApiDocsFixture.PackageSnapshot(
            Path.Combine(_root, "missing-corpus"), definition) with
        {
            State = productionSnapshot.State with { SchemaVersion = 1 },
        };
        var structurallyIncomplete = ApiDocsFixture.PackageSnapshot(
            Path.Combine(_root, "missing-corpus-structure"),
            definition,
            sourceRef: "head:main") with
        {
            State = productionSnapshot.State with
            {
                Ref = "head:main",
                FetchedAt = default,
            },
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(wrongSchema, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(structurallyIncomplete, definition.PackageId, "net10.0"));
    }

    [TestMethod]
    public void SnapshotPackageJoinRejectsMissingAndDuplicateDefinitionsAndStates()
    {
        var definition = ApiDocsFixture.PackageDefinition();
        var state = ApiDocsFixture.PackageState();

        var missingDefinition = ApiDocsFixture.PackageSnapshot(
            _root, definition, state, configuredPackages: []);
        var duplicateDefinitions = ApiDocsFixture.PackageSnapshot(
            _root, definition, state, configuredPackages: [definition, definition]);
        var missingState = ApiDocsFixture.PackageSnapshot(
            _root, definition, state, synchronizedPackages: []);
        var duplicateStates = ApiDocsFixture.PackageSnapshot(
            _root, definition, state, synchronizedPackages: [state, state]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(missingDefinition, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(duplicateDefinitions, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(missingState, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(duplicateStates, definition.PackageId, "net10.0"));
    }

    [TestMethod]
    public void SnapshotRejectsInvalidRefAndPackageStateStructureBeforeCorpusAccess()
    {
        var definition = ApiDocsFixture.PackageDefinition();
        var state = ApiDocsFixture.PackageState();
        var invalidFeedDefinition = definition with { Feed = "http://feed.test/v3/index.json" };
        var invalidFeedState = state with { Feed = invalidFeedDefinition.Feed };
        var invalidFeed = ApiDocsFixture.PackageSnapshot(
            _root, invalidFeedDefinition, invalidFeedState);
        var staleRef = ApiDocsFixture.PackageSnapshot(
            _root, definition, state, sourceRef: "head:other");
        var duplicateFrameworks = ApiDocsFixture.PackageSnapshot(
            _root,
            definition,
            state with { AvailableFrameworks = ["net10.0", "NET10.0"] });

        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(invalidFeed, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(staleRef, definition.PackageId, "net10.0"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            new PackageApiDocsBackend(duplicateFrameworks, definition.PackageId, "net10.0"));
    }

    [TestMethod]
    public void SnapshotRejectsNullPackageStateDefaultFetchedAtAndUnsafeFrameworksBeforeCorpusAccess()
    {
        var definition = ApiDocsFixture.PackageDefinition();
        var state = ApiDocsFixture.PackageState();
        var cases = new[]
        {
            ApiDocsFixture.PackageSnapshot(
                Path.Combine(_root, "missing-null-state"), definition, state,
                synchronizedPackages: [null!]),
            ApiDocsFixture.PackageSnapshot(
                Path.Combine(_root, "missing-default-fetched"), definition,
                state with { FetchedAt = default }),
            ApiDocsFixture.PackageSnapshot(
                Path.Combine(_root, "missing-reserved-framework"),
                definition with { DefaultFramework = "CON" },
                state with { DefaultFramework = "CON", AvailableFrameworks = ["CON"] }),
            ApiDocsFixture.PackageSnapshot(
                Path.Combine(_root, "missing-trailing-framework"),
                definition with { DefaultFramework = "net10.0." },
                state with { DefaultFramework = "net10.0.", AvailableFrameworks = ["net10.0."] }),
        };

        foreach (var snapshot in cases)
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                new PackageApiDocsBackend(snapshot, definition.PackageId, snapshot.State.ApiPackages![0]?.DefaultFramework ?? "net10.0"));
        }
    }

    [TestMethod]
    public async Task OperationsHonorPreCanceledTokens()
    {
        var backend = await ApiDocsFixture.CreatePackageBackendAsync(_root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => backend.Lookup("System.Widget", cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => backend.Search("Widget", cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => backend.SearchText("widget", cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => backend.FindReferences("System.String", cancellation.Token));
    }

    private static string ReferenceKey(ApiReferenceHitRead item) => string.Join(
        "\0",
        item.DeclarationId,
        item.Symbol,
        item.Kind,
        item.ParameterName,
        item.TypeExpression,
        item.AttributeType,
        item.Signature);
}
