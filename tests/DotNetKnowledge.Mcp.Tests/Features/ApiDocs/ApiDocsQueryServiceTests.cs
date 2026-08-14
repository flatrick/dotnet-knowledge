using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;
using static DotNetKnowledge.Mcp.Tests.Features.ApiDocs.ApiDocsFixture;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiDocsQueryServiceTests
{
    private static readonly string[] ExpectedFirstPageNames = ["System.AlphaWidget", "System.BetaWidget"];
    private static readonly string[] ExpectedSecondPageNames = ["System.GammaWidget"];
    private static readonly string[] ExpectedResolvedTypeNames = ["System.Widget"];
    private static readonly string[] ExpectedHolderTypes = ["System.Holder", "System.Holder<T>"];
    private static readonly string[] ExpectedWidgetTypes =
        ["System.Widget", "System.WidgetKit", "System.WidgetPolicy`1"];
    private static readonly string[] ExpectedSystemNamespaceTypes =
        ["System.Widget", "System.WidgetKit", "System.WidgetPolicy`1", "System.Widgets.Gadget"];
    private static readonly string[] ExpectedStringParameterTypes =
    [
        "System.String",
        "System.String[]",
        "System.Collections.Generic.IEnumerable<System.String>",
    ];
    private static readonly string[] ExpectedCanonicalPackageOrder =
        ["Fixture.Alpha", "Fixture.Zeta"];
    private static readonly string[] ExpectedReversedPackageOrder =
        ["Fixture.Zeta", "Fixture.Alpha"];

    [TestMethod]
    public async Task SearchAsyncReturnsDeterministicPagesWithoutBodies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            var namespaceDirectory = Path.Combine(repository, "xml", "System");
            Directory.CreateDirectory(namespaceDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            foreach (var name in new[] { "GammaWidget", "AlphaWidget", "BetaWidget" })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(namespaceDirectory, name + ".xml"),
                    $"<Type Name=\"{name}\" FullName=\"System.{name}\" />");
            }
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "docs");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
            var service = new ApiDocsQueryService(catalog, cache, synchronizer);

            var first = await service.SearchAsync("Widget", limit: 2, cursor: null, CancellationToken.None);

            CollectionAssert.AreEqual(
                ExpectedFirstPageNames,
                first.Items.Select(item => item.Name).ToArray());
            Assert.IsTrue(first.IsPartial);
            Assert.IsNotNull(first.NextPageToken);
            Assert.AreEqual("test/dotnet-api-docs", ((GitProvenance)first.Items[0].Source).Repo);
            Assert.HasCount(1, first.SearchedSources);
            Assert.AreEqual(pin, ((GitProvenance)first.SearchedSources[0]).Commit);

            var malformedCursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    "{\"Version\":1,\"Pattern\":\"Widget\",\"Offset\":0}"))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var cursorException = await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", limit: 2, cursor: malformedCursor, CancellationToken.None));
            Assert.AreEqual("cursor", cursorException.ParamName);

            var second = await service.SearchAsync(
                "Widget",
                limit: 2,
                cursor: first.NextPageToken,
                CancellationToken.None);

            CollectionAssert.AreEqual(
                ExpectedSecondPageNames,
                second.Items.Select(item => item.Name).ToArray());
            Assert.IsFalse(second.IsPartial);
            Assert.IsNull(second.NextPageToken);

            await File.WriteAllTextAsync(Path.Combine(namespaceDirectory, "DeltaWidget.xml"),
                "<Type Name=\"DeltaWidget\" FullName=\"System.DeltaWidget\" />");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "updated docs");
            await synchronizer.SyncAsync("dotnet-api-docs", "head", CancellationToken.None);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", limit: 2, cursor: first.NextPageToken, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupAsyncReturnsDocumentedMemberWithProvenance()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // The helper commits the fixture and computes its own pin internally; fetch it
            // independently here so the assertions below can still verify it end-to-end.
            var pin = (await RunGitAsync(Path.Combine(root, "origin"), "rev-parse", "HEAD")).Trim();

            var result = await service.LookupAsync(
                "Widget.Create",
                "dotnet-api-docs",
                limit: 20,
                cursor: null,
                CancellationToken.None);

            Assert.HasCount(1, result.Matches);
            var match = result.Matches[0];
            Assert.AreEqual("System.Widget", match.FullName);
            Assert.HasCount(1, match.Members);
            var member = match.Members[0];
            Assert.AreEqual("public static System.Widget Create(string name);", member.Signature);
            Assert.AreEqual("Creates a widget.", member.Summary);
            Assert.AreEqual("The widget name.", member.Parameters![0].Description);
            Assert.AreEqual("The new widget.", member.Returns);
            Assert.AreEqual("Names are case-sensitive.", member.Remarks);
            var source = (GitProvenance)match.Source;
            Assert.AreEqual("test/dotnet-api-docs", source.Repo);
            Assert.AreEqual("pinned", source.Ref);
            Assert.AreEqual(pin, source.Commit);

            var missing = await service.LookupAsync(
                "System.MissingWidget",
                "dotnet-api-docs",
                limit: 20,
                cursor: null,
                CancellationToken.None);
            Assert.IsEmpty(missing.Matches);
            Assert.HasCount(1, missing.SearchedSources);
            Assert.AreEqual(pin, ((GitProvenance)missing.SearchedSources[0]).Commit);

            foreach (var maliciousSymbol in new[] { "../Widget", "..\\Widget", "System.*", "C:\\Widget" })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                    maliciousSymbol,
                    "dotnet-api-docs",
                    limit: 20,
                    cursor: null,
                    CancellationToken.None));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncMatchesNamespacesBySegmentAndTypeNamesBySubstring()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // A fully-qualified name: the caller copied it out of a compiler error.
            var byFullName = await Search(service, "System.Widget");
            CollectionAssert.AreEqual(ExpectedResolvedTypeNames, byFullName.Select(item => item.Name).ToArray());
            Assert.AreEqual(ApiNameMatch.FullName, byFullName[0].MatchedOn);

            // A namespace names everything under it, descendants included, and says that is what
            // it did.
            var byNamespace = await Search(service, "System");
            CollectionAssert.AreEqual(
                ExpectedSystemNamespaceTypes,
                byNamespace.Select(item => item.Name).ToArray());
            Assert.IsTrue(byNamespace.All(item => item.MatchedOn == ApiNameMatch.Namespace));

            // Which of them System itself declares is reported, not filtered.
            Assert.AreEqual(
                0,
                byNamespace.Single(item => item.Name == "System.Widget").NamespaceDepth);
            Assert.AreEqual(
                1,
                byNamespace.Single(item => item.Name == "System.Widgets.Gadget").NamespaceDepth);

            // Depth belongs to the namespace reading alone; a type match did not name a namespace.
            Assert.IsNull((await Search(service, "Widget"))[0].NamespaceDepth);

            // A type-name fragment still matches on any substring, and outranks the namespace
            // reading when both apply.
            var byFragment = await Search(service, "idget");
            CollectionAssert.AreEqual(ExpectedWidgetTypes, byFragment.Select(item => item.Name).ToArray());
            Assert.IsTrue(byFragment.All(item => item.MatchedOn == ApiNameMatch.Type));

            // A namespace fragment that is not a whole segment names nothing. This is the blast
            // radius the segment rule exists to prevent.
            Assert.IsEmpty(await Search(service, "Syst"));

            // A single segment equal to the type name is a type match spelled exactly, not a
            // whole-name match.
            Assert.AreEqual(ApiNameMatch.Type, (await Search(service, "Widget"))[0].MatchedOn);

            // A pattern with an empty segment names nothing.
            Assert.IsEmpty(await Search(service, "System..Widget"));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<ApiSearchItem[]> Search(ApiDocsQueryService service, string pattern)
    {
        var result = await service.SearchAsync(pattern, limit: 100, cursor: null, CancellationToken.None);
        return result.Items.ToArray();
    }

    [TestMethod]
    public async Task SearchAsyncNotesADottedPatternThatMatchedNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // "Widget.Create" is Widget's member Create, not a type name — search_api matches type
            // names only, so it returns nothing. The empty set must not read as "no such API".
            var memberQualified = await service.SearchAsync(
                "Widget.Create", limit: 100, cursor: null, CancellationToken.None);
            Assert.IsEmpty(memberQualified.Items);
            Assert.IsNotNull(memberQualified.Note);
            StringAssert.Contains(memberQualified.Note.Message, "lookup_api");

            // "System.WidgetPolicy" is the generic type WidgetPolicy`1's name without its arity, so
            // the whole-name match misses; the note points at searching the simple name.
            var genericFullName = await service.SearchAsync(
                "System.WidgetPolicy", limit: 100, cursor: null, CancellationToken.None);
            Assert.IsEmpty(genericFullName.Items);
            Assert.IsNotNull(genericFullName.Note);
            StringAssert.Contains(genericFullName.Note.Message, "WidgetPolicy");

            // A pattern that matched carries no note.
            var matched = await service.SearchAsync(
                "Widget", limit: 100, cursor: null, CancellationToken.None);
            Assert.IsNotEmpty(matched.Items);
            Assert.IsNull(matched.Note);

            // An undotted miss has no member/generic reading to suggest.
            var undotted = await service.SearchAsync(
                "Nonesuch", limit: 100, cursor: null, CancellationToken.None);
            Assert.IsEmpty(undotted.Items);
            Assert.IsNull(undotted.Note);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchTextAsyncMatchesRenderedProseAndNamesTheOwningSymbol()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var bySummary = await SearchText(service, "Creates a widget");
            Assert.HasCount(1, bySummary);
            Assert.AreEqual("System.Widget.Create", bySummary[0].Symbol);
            Assert.AreEqual("summary", bySummary[0].Element);
            Assert.AreEqual("Creates a widget.", bySummary[0].Text);
            Assert.IsFalse(bySummary[0].IsTruncated);
            Assert.AreEqual("test/dotnet-api-docs", ((GitProvenance)bySummary[0].Source).Repo);

            // Remarks are searched. Leaving them out would answer "no" to a question whose answer
            // is in the corpus.
            var byRemarks = await SearchText(service, "case-sensitive");
            Assert.HasCount(1, byRemarks);
            Assert.AreEqual("remarks", byRemarks[0].Element);

            // A parameter description reports which parameter it was.
            var byParam = await SearchText(service, "The widget name");
            Assert.AreEqual("param:name", byParam[0].Element);

            // Matching runs on the RENDERED text, so a phrase that only exists once a cref has been
            // resolved is findable — it spans an element boundary in the raw XML and the whole
            // query is therefore not present in the file at all.
            var acrossReference = await SearchText(service, "widget as a System.String");
            Assert.HasCount(1, acrossReference);
            Assert.AreEqual("System.Widget.Describe", acrossReference[0].Symbol);

            Assert.IsEmpty(await SearchText(service, "no such prose anywhere"));

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchTextAsync(
                "   ", source: null, limit: 20, cursor: null, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SearchTextAsync(
                "widget", source: null, limit: 101, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<ApiTextHit[]> SearchText(ApiDocsQueryService service, string query)
    {
        var result = await service.SearchTextAsync(
            query, source: "dotnet-api-docs", limit: 100, cursor: null, CancellationToken.None);
        return result.Hits.ToArray();
    }

    [TestMethod]
    public async Task FindReferencesAsyncFindsStructuralUsesIncludingCompoundTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var all = await FindReferences(service, "System.String", kind: null);

            // Widget.Create takes a bare string; Widget.Describe takes one too. WidgetKit declares
            // string[] and IEnumerable<System.String> parameters and a System.String& return, none
            // of which an equality test would find.
            var parameters = all.Where(hit => hit.Kind == ApiReferenceKind.Parameter).ToArray();
            CollectionAssert.AreEquivalent(
                ExpectedStringParameterTypes,
                parameters.Select(hit => hit.TypeExpression).Distinct().ToArray());

            var returns = all.Where(hit => hit.Kind == ApiReferenceKind.Return).ToArray();
            Assert.IsTrue(returns.Any(hit => hit.TypeExpression == "System.String&"));

            // A near-miss name must not match.
            Assert.IsFalse(
                all.Any(hit => hit.TypeExpression is not null && hit.TypeExpression.Contains("StringComparer", StringComparison.Ordinal)),
                "System.StringComparer is a different type and must not match System.String.");

            // Base and interface are reported, and are not parameters.
            var derived = await FindReferences(service, "System.WidgetBase", kind: null);
            Assert.AreEqual(ApiReferenceKind.Base, derived.Single().Kind);
            Assert.AreEqual("System.WidgetKit", derived.Single().Symbol);

            var implementors = await FindReferences(service, "System.IWidget", kind: null);
            Assert.AreEqual(ApiReferenceKind.Interface, implementors.Single().Kind);

            // kind filters the page, and a hit names the parameter it came from.
            var onlyParameters = await FindReferences(service, "System.String", ApiReferenceKind.Parameter);
            Assert.IsTrue(onlyParameters.All(hit => hit.Kind == ApiReferenceKind.Parameter));
            Assert.IsTrue(onlyParameters.Any(hit => hit.ParameterName == "name"));

            // Totals describe the whole set, not the filtered page — otherwise a caller narrowing to
            // parameters could not see that anything derives from the type.
            var filtered = await service.FindReferencesAsync(
                "System.String", ApiReferenceKind.Parameter, null, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(parameters.Length, filtered.Totals.Parameter);
            Assert.AreEqual(returns.Length, filtered.Totals.Return);

            // A type is not a reference to itself.
            Assert.IsFalse((await FindReferences(service, "System.Widget", kind: null))
                .Any(hit => hit.Symbol.StartsWith("System.Widget.", StringComparison.Ordinal)
                    && hit.Kind == ApiReferenceKind.Base));

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.FindReferencesAsync(
                "System.String", "nonsense", null, "dotnet-api-docs", 20, null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FindReferencesAsyncSeparatesTheTypeItselfFromExpressionsParameterizedByIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var all = await FindReferences(service, "System.String", kind: null);
            Assert.IsTrue(all.Where(hit => hit.TypeExpression == "System.String").All(hit => hit.IsExact));
            Assert.IsFalse(all.Where(hit => hit.TypeExpression != "System.String").Any(hit => hit.IsExact));

            // "what implements System.IWidget" and "what implements something parameterized by it"
            // are different questions, so the distinction filters as well as reports.
            var exact = await FindReferences(service, "System.String", kind: null, exact: true);
            CollectionAssert.AreEqual(
                all.Where(hit => hit.IsExact).Select(hit => hit.Symbol).ToArray(),
                exact.Select(hit => hit.Symbol).ToArray());

            var parameterized = await FindReferences(service, "System.String", kind: null, exact: false);
            Assert.IsNotEmpty(parameterized);
            Assert.IsFalse(parameterized.Any(hit => hit.IsExact));

            // Totals still describe the whole matched set, before either filter narrows it.
            var filtered = await service.FindReferencesAsync(
                "System.String", null, true, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(
                all.Count(hit => hit.Kind == ApiReferenceKind.Parameter),
                filtered.Totals.Parameter);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FindReferencesAsyncReadsGenericConstraintsAndAttributeApplications()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // A constraint is how an API says a type is a required capability, and it sits in a
            // TypeParameter rather than in Base. Both the base-type and the interface form count.
            var constrainingBase = await FindReferences(service, "System.WidgetPolicyBase", kind: null);
            Assert.AreEqual(ApiReferenceKind.Constraint, constrainingBase.Single().Kind);
            Assert.AreEqual("TWidget", constrainingBase.Single().ParameterName);

            var constrainingInterface = await FindReferences(service, "System.IWidgetPolicy", kind: null);
            Assert.AreEqual(ApiReferenceKind.Constraint, constrainingInterface.Single().Kind);

            // Members carry their own type parameters, so a generic method is reached too.
            var constrainingMember = await FindReferences(service, "System.WidgetState", kind: null);
            Assert.AreEqual("System.WidgetPolicy<TWidget>.Adapt<TState>", constrainingMember.Single().Symbol);

            // Both applications name the attribute itself, whether or not they carry arguments.
            var decorating = await FindReferences(service, "System.WidgetMarker", kind: null);
            Assert.HasCount(2, decorating);
            Assert.IsTrue(decorating.All(hit => hit.Kind == ApiReferenceKind.Attribute && hit.IsExact));

            // A type named inside an attribute's arguments is a reference, and is not the
            // attribute the declaration is decorated with.
            var inArguments = await FindReferences(service, "System.String", ApiReferenceKind.Attribute);
            Assert.AreEqual(
                "[System.WidgetMarker(typeof(System.String))]",
                inArguments.Single().TypeExpression);
            Assert.IsFalse(inArguments.Single().IsExact);

            // Which attribute that was is a fact the application text alone leaves the caller to
            // parse back out.
            Assert.AreEqual("System.WidgetMarker", inArguments.Single().AttributeType);

            // Totals name every kind, so a caller filtering to one still sees the others exist.
            var marker = await service.FindReferencesAsync(
                "System.WidgetMarker", null, null, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(2, marker.Totals.Attribute);
            Assert.AreEqual(0, marker.Totals.Constraint);

            // Nothing here is named System.WidgetMarkerAttribute, so the short form has one reading
            // and there is nothing to report having left out.
            Assert.IsNull(marker.Note);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FindReferencesAsyncFindsAnApplicationByTheAttributesClrName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateAttributeSiblingServiceAsync(root);

            // ECMA XML records an application the way C# spells it, so the CLR name is the one
            // string that never appears in it. A caller who spelled the type correctly must still
            // find the applications rather than be told the attribute is unused.
            var byClrName = await FindReferences(
                service, "System.WidgetSealAttribute", ApiReferenceKind.Attribute);
            Assert.HasCount(1, byClrName);
            Assert.AreEqual("[System.WidgetSeal]", byClrName[0].TypeExpression);
            Assert.IsTrue(byClrName[0].IsExact);

            // typeExpression is the source spelling and attributeType the identity, so a caller
            // never has to know which of the two it is holding.
            Assert.AreEqual("System.WidgetSealAttribute", byClrName[0].AttributeType);

            // The short form names no type at all here, exactly as System.Obsolete does not, and
            // still gets the note rather than a bare zero.
            var byShortForm = await service.FindReferencesAsync(
                "System.WidgetSeal", null, null, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(0, byShortForm.Totals.Attribute);
            Assert.AreEqual("System.WidgetSealAttribute", byShortForm.Note?.SiblingType);
            Assert.AreEqual(1, byShortForm.Note?.AttributeApplications);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FindReferencesAsyncDoesNotCreditAnApplicationToTheDeSuffixedSibling()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateAttributeSiblingServiceAsync(root);

            // [System.WidgetTrait] is an application of System.WidgetTraitAttribute. The class of
            // that name is a different type, and counting the application as a reference to it is a
            // wrong answer rather than a missing one.
            var colliding = await service.FindReferencesAsync(
                "System.WidgetTrait", null, null, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(0, colliding.Totals.Attribute);

            // The class's own structural uses are untouched by that exclusion.
            Assert.AreEqual(1, colliding.Totals.Parameter);

            // Excluding them silently would be the plausible absence again, so the response names
            // the sibling, counts what it left out, and names the call that reaches it.
            Assert.IsNotNull(colliding.Note);
            Assert.AreEqual("System.WidgetTraitAttribute", colliding.Note.SiblingType);
            Assert.AreEqual(1, colliding.Note.AttributeApplications);
            StringAssert.Contains(colliding.Note.Remedy, "find_api_references");
            StringAssert.Contains(colliding.Note.Remedy, "System.WidgetTraitAttribute");

            // And that call is the one that answers.
            var sibling = await service.FindReferencesAsync(
                "System.WidgetTraitAttribute", null, null, "dotnet-api-docs", 100, null, CancellationToken.None);
            Assert.AreEqual(1, sibling.Totals.Attribute);
            Assert.AreEqual("System.WidgetTraitAttribute", sibling.Hits.Single().AttributeType);

            // Nothing is de-suffixed twice, so the sibling's own answer carries no note.
            Assert.IsNull(sibling.Note);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<ApiReferenceHit[]> FindReferences(
        ApiDocsQueryService service,
        string symbol,
        string? kind,
        bool? exact = null)
    {
        var result = await service.FindReferencesAsync(
            symbol, kind, exact, "dotnet-api-docs", limit: 100, cursor: null, CancellationToken.None);
        return result.Hits.ToArray();
    }

    [TestMethod]
    public async Task LookupAsyncResolvesReferenceElementsToTheNamesTheyName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            var result = await service.LookupAsync(
                "Widget.Describe", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);

            var member = result.Matches[0].Members[0];

            // Taking XElement.Value here would leave "Renders the widget as a ." — a sentence that
            // still reads as complete while missing the type it names.
            Assert.AreEqual("Renders the widget as a System.String.", member.Summary);
            Assert.AreEqual("The label applied to name.", member.Parameters![0].Description);
            Assert.AreEqual("A System.String, or null.", member.Returns);

            // A cref is kept whole so the caller can feed it straight back to lookup_api.
            Assert.AreEqual("See also System.Widget.Create(System.String).", member.Remarks);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupAsyncMatchesGenericMembersByPlainNameAndSeparatesMissingKinds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // Every arity of a requested name comes back. A caller asking for "Convert" cannot
            // otherwise discover which arities exist.
            var byPlainName = await service.LookupAsync(
                "Widget.Convert", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, byPlainName.Outcome);
            Assert.HasCount(2, byPlainName.Matches[0].Members);

            // The fully-specified form still matches, and selects one arity.
            var bySpecificArity = await service.LookupAsync(
                "Widget.Convert<TResult>", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, bySpecificArity.Outcome);
            Assert.HasCount(1, bySpecificArity.Matches[0].Members);

            // A type that does not exist and a member that does not exist are different failures.
            var noSuchType = await service.LookupAsync(
                "System.MissingWidget", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.TypeNotFound, noSuchType.Outcome);
            Assert.IsEmpty(noSuchType.ResolvedTypeNames);

            var noSuchMember = await service.LookupAsync(
                "Widget.NotAMember", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.MemberNotFound, noSuchMember.Outcome);
            CollectionAssert.AreEqual(ExpectedResolvedTypeNames, noSuchMember.ResolvedTypeNames.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupAsyncDecidesTheDetailTierPerSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateOverlappingSourcesServiceAsync(root);

            // One string, two readings: dotnet-api-docs resolves System.Widget's member Create,
            // roslyn-api-docs resolves a type of that name.
            var result = await service.LookupAsync(
                "System.Widget.Create", source: null, limit: 100, cursor: null, CancellationToken.None);

            Assert.AreEqual(ApiLookupOutcome.Found, result.Outcome);
            var asMember = result.Matches.Single(match => match.FullName == "System.Widget");
            var asType = result.Matches.Single(match => match.FullName == "System.Widget.Create");

            // Naming a member is how a caller asks for its documentation, and the other source's
            // reading of the same string must not take it away.
            Assert.AreEqual("Creates a widget.", asMember.Members.Single().Summary);
            Assert.AreEqual(ApiLookupDetail.Full, asMember.Detail);

            // A bare type name is an inventory request, and stays one. Detail is what lets a
            // caller tell this from a signatures-only decision made for the other match.
            Assert.IsNull(asType.Members.Single().Summary);
            Assert.AreEqual(ApiLookupDetail.Signatures, asType.Detail);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupAsyncBudgetsWholeTypeResponsesAndPaginates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateWidgetServiceAsync(root);

            // A bare type name is signatures only. remarks is the largest contributor to response
            // size and appears only when a caller named the member it belongs to.
            var wholeType = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, wholeType.Outcome);
            Assert.HasCount(4, wholeType.Matches[0].Members);
            foreach (var member in wholeType.Matches[0].Members)
            {
                Assert.IsNotNull(member.Signature);
                Assert.IsNull(member.Summary);
                Assert.IsNull(member.Remarks);
                Assert.IsNull(member.Parameters);
            }

            // Naming a member restores full documentation.
            var oneMember = await service.LookupAsync(
                "Widget.Create", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);
            Assert.AreEqual("Creates a widget.", oneMember.Matches[0].Members[0].Summary);
            Assert.AreEqual("Names are case-sensitive.", oneMember.Matches[0].Members[0].Remarks);
            Assert.AreEqual("The widget name.", oneMember.Matches[0].Members[0].Parameters![0].Description);

            // Paging is over a flat member sequence, so a page boundary can fall inside a type.
            var firstPage = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, cursor: null, CancellationToken.None);
            Assert.HasCount(2, firstPage.Matches[0].Members);
            Assert.IsTrue(firstPage.IsPartial);
            Assert.IsNotNull(firstPage.NextPageToken);

            var secondPage = await service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, firstPage.NextPageToken, CancellationToken.None);
            Assert.HasCount(2, secondPage.Matches[0].Members);
            Assert.IsFalse(secondPage.IsPartial);
            Assert.IsNull(secondPage.NextPageToken);

            // A cursor issued for one symbol must not be honored for another.
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                "Widget.Create", "dotnet-api-docs", limit: 2, firstPage.NextPageToken, CancellationToken.None));

            // A search cursor must not be honored by lookup.
            var search = await service.SearchAsync("Widget", limit: 1, cursor: null, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                "Widget", "dotnet-api-docs", limit: 2, search.NextPageToken, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LookupAsyncReturnsBothTheNonGenericTypeAndItsGenericNamesake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var repository = Path.Combine(root, "origin");
            var namespaceDirectory = Path.Combine(repository, "xml", "System");
            Directory.CreateDirectory(namespaceDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(
                Path.Combine(namespaceDirectory, "Holder.xml"),
                "<Type Name=\"Holder\" FullName=\"System.Holder\"><TypeSignature Language=\"DocId\" Value=\"T:System.Holder\" /></Type>");
            await File.WriteAllTextAsync(
                Path.Combine(namespaceDirectory, "Holder`1.xml"),
                "<Type Name=\"Holder`1\" FullName=\"System.Holder&lt;T&gt;\"><TypeSignature Language=\"DocId\" Value=\"T:System.Holder`1\" /></Type>");
            await RunGitAsync(repository, "add", ".");
            await RunGitAsync(repository, "commit", "-m", "docs");
            var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, repository, pin);
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
            var service = new ApiDocsQueryService(catalog, cache, synchronizer);

            var result = await service.LookupAsync(
                "Holder", "dotnet-api-docs", limit: 20, cursor: null, CancellationToken.None);

            Assert.AreEqual(ApiLookupOutcome.Found, result.Outcome);
            CollectionAssert.AreEqual(
                ExpectedHolderTypes,
                result.Matches.Select(match => match.FullName).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FrameworkSelectionIsCanonicalAndCoveredAcrossEveryOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);

            var lookupDefault = await service.LookupAsync(
                "StringDerived", "roslyn-api-docs", framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net10.0", lookupDefault.EffectiveFramework, lookupDefault.DefaultFramework,
                lookupDefault.AvailableFrameworks);
            Assert.IsInstanceOfType<NuGetProvenance>(lookupDefault.Matches.Single().Source);

            var lookupCase = await service.LookupAsync(
                "LegacyWidget", "roslyn-api-docs", "NET8.0",
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net8.0", lookupCase.EffectiveFramework, lookupCase.DefaultFramework,
                lookupCase.AvailableFrameworks);

            var searchDefault = await service.SearchAsync(
                "StringDerived", framework: null, limit: 20, cursor: null,
                cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net10.0", searchDefault.EffectiveFramework, searchDefault.DefaultFramework,
                searchDefault.AvailableFrameworks);
            Assert.AreEqual("System.StringDerived", searchDefault.Items.Single().Name);

            var searchCase = await service.SearchAsync(
                "LegacyWidget", "NET8.0", limit: 20, cursor: null,
                cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net8.0", searchCase.EffectiveFramework, searchCase.DefaultFramework,
                searchCase.AvailableFrameworks);
            Assert.AreEqual("System.LegacyWidget", searchCase.Items.Single().Name);

            var textDefault = await service.SearchTextAsync(
                "Needle", "roslyn-api-docs", framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net10.0", textDefault.EffectiveFramework, textDefault.DefaultFramework,
                textDefault.AvailableFrameworks);
            Assert.IsNotEmpty(textDefault.Hits);

            var textCase = await service.SearchTextAsync(
                "missing", "roslyn-api-docs", "NET8.0",
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            Assert.IsEmpty(textCase.Hits);
            AssertFrameworkCoverage(
                "net8.0", textCase.EffectiveFramework, textCase.DefaultFramework,
                textCase.AvailableFrameworks);

            var referencesDefault = await service.FindReferencesAsync(
                "System.String", kind: null, exact: null, source: "roslyn-api-docs", framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            AssertFrameworkCoverage(
                "net10.0", referencesDefault.EffectiveFramework, referencesDefault.DefaultFramework,
                referencesDefault.AvailableFrameworks);
            Assert.IsNotEmpty(referencesDefault.Hits);

            var referencesCase = await service.FindReferencesAsync(
                "System.String", kind: null, exact: null, source: "roslyn-api-docs", framework: "NET8.0",
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            Assert.IsNotEmpty(referencesCase.Hits);
            Assert.IsTrue(referencesCase.Hits.All(hit => hit.Source is GitProvenance));
            AssertFrameworkCoverage(
                "net8.0", referencesCase.EffectiveFramework, referencesCase.DefaultFramework,
                referencesCase.AvailableFrameworks);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task FrameworkValidationRejectsUnknownValuesAndFrameworkNeutralSourceFilters()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);
            var unknownCalls = new Func<Task>[]
            {
                () => service.LookupAsync(
                    "Widget", "roslyn-api-docs", "net7.0", 20, null, CancellationToken.None),
                () => service.SearchAsync("Widget", "net7.0", 20, null, CancellationToken.None),
                () => service.SearchTextAsync(
                    "widget", "roslyn-api-docs", "net7.0", 20, null, CancellationToken.None),
                () => service.FindReferencesAsync(
                    "System.String", null, null, "roslyn-api-docs", "net7.0",
                    20, null, CancellationToken.None),
            };
            foreach (var call in unknownCalls)
            {
                var error = await Assert.ThrowsExactlyAsync<FrameworkNotAvailableException>(call);
                Assert.AreEqual("net7.0", error.RequestedFramework);
                Assert.AreEqual("net10.0", error.DefaultFramework);
                CollectionAssert.AreEqual(PackageFrameworks, error.AvailableFrameworks.ToArray());
            }

            var neutralCalls = new Func<Task>[]
            {
                () => service.LookupAsync(
                    "Widget", "dotnet-api-docs", "net8.0", 20, null, CancellationToken.None),
                () => service.SearchTextAsync(
                    "widget", "dotnet-api-docs", "net8.0", 20, null, CancellationToken.None),
                () => service.FindReferencesAsync(
                    "System.String", null, null, "dotnet-api-docs", "net8.0",
                    20, null, CancellationToken.None),
            };
            foreach (var call in neutralCalls)
            {
                var error = await Assert.ThrowsExactlyAsync<ArgumentException>(call);
                Assert.AreEqual("framework", error.ParamName);
            }

            var allSourceSearch = await service.SearchAsync(
                "Widget", "net8.0", limit: 100, cursor: null,
                cancellationToken: CancellationToken.None);
            Assert.IsTrue(allSourceSearch.Items.Any(item => item.Name == "System.Widget"
                && item.Source is GitProvenance));
            Assert.IsTrue(allSourceSearch.Items.Any(item => item.Name == "System.LegacyWidget"
                && item.Source is NuGetProvenance));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CompleteBackendReadsMergeWithGitPrecedenceAndStableDeduplication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        var reversedRoot = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);
            var reversed = await CreateMergedServiceAsync(reversedRoot, reverseFixtureInsertion: true);

            var lookup = await service.LookupAsync(
                "Widget", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.HasCount(1, lookup.Matches);
            Assert.IsInstanceOfType<GitProvenance>(lookup.Matches.Single().Source);
            Assert.HasCount(5, lookup.Matches.Single().Members);
            var create = lookup.Matches.Single().Members.Single(member => member.Name == "Create");
            Assert.IsInstanceOfType<GitProvenance>(create.Source);
            var packageOnly = lookup.Matches.Single().Members.Single(
                member => member.Name == "PackageOnly");
            Assert.AreEqual("Fixture.Package", ((NuGetProvenance)packageOnly.Source).PackageId);

            var packageOnlyMember = await service.LookupAsync(
                "Widget.PackageOnly", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.AreEqual(ApiLookupOutcome.Found, packageOnlyMember.Outcome);
            Assert.HasCount(1, packageOnlyMember.Matches);
            Assert.IsInstanceOfType<GitProvenance>(packageOnlyMember.Matches.Single().Source);
            Assert.AreEqual(
                "Fixture.Package",
                ((NuGetProvenance)packageOnlyMember.Matches.Single().Members.Single().Source).PackageId);

            var firstLookupPage = await service.LookupAsync(
                "Widget", "roslyn-api-docs", framework: null,
                limit: 4, cursor: null, cancellationToken: CancellationToken.None);
            var secondLookupPage = await service.LookupAsync(
                "Widget", "roslyn-api-docs", framework: null,
                limit: 4, cursor: firstLookupPage.NextPageToken,
                cancellationToken: CancellationToken.None);
            Assert.IsTrue(firstLookupPage.IsPartial);
            Assert.IsFalse(secondLookupPage.IsPartial);
            Assert.AreEqual(
                5,
                firstLookupPage.Matches.Sum(match => match.Members.Count)
                    + secondLookupPage.Matches.Sum(match => match.Members.Count));
            var packageOnlyLookup = await service.LookupAsync(
                "StringDerived", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.IsInstanceOfType<NuGetProvenance>(packageOnlyLookup.Matches.Single().Source);

            var search = await service.SearchAsync(
                "Widget", framework: null, limit: 100, cursor: null,
                cancellationToken: CancellationToken.None);
            Assert.HasCount(1, search.Items.Where(item => item.Name == "System.Widget"));
            Assert.IsInstanceOfType<GitProvenance>(
                search.Items.Single(item => item.Name == "System.Widget").Source);
            Assert.IsTrue(search.Items.Any(item => item.Name == "System.WidgetSealAttribute"
                && item.Source is NuGetProvenance));
            var reversedSearch = await reversed.SearchAsync(
                "Widget", framework: null, limit: 100, cursor: null,
                cancellationToken: CancellationToken.None);
            CollectionAssert.AreEqual(
                search.Items.Select(item => item.Name).ToArray(),
                reversedSearch.Items.Select(item => item.Name).ToArray());

            var text = await service.SearchTextAsync(
                "Creates a widget", source: null, framework: null,
                limit: 1, cursor: null, cancellationToken: CancellationToken.None);
            Assert.HasCount(1, text.Hits);
            Assert.IsFalse(text.IsPartial);
            Assert.IsInstanceOfType<GitProvenance>(text.Hits.Single().Source);

            var references = await service.FindReferencesAsync(
                "System.String", kind: null, exact: null, source: null, framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.HasCount(16, references.Hits);
            Assert.AreEqual(6, references.Totals.Parameter);
            Assert.AreEqual(3, references.Totals.Return);
            Assert.AreEqual(2, references.Totals.Base);
            Assert.AreEqual(2, references.Totals.Interface);
            Assert.AreEqual(1, references.Totals.Constraint);
            Assert.AreEqual(2, references.Totals.Attribute);
            Assert.HasCount(1, references.Hits.Where(hit => hit.Symbol == "System.Widget.Create"
                && hit.Kind == ApiReferenceKind.Parameter));
            Assert.IsInstanceOfType<GitProvenance>(references.Hits.Single(hit =>
                hit.Symbol == "System.Widget.Create" && hit.Kind == ApiReferenceKind.Parameter).Source);
            Assert.IsTrue(references.Hits.Any(hit => hit.Symbol == "System.StringDerived"
                && hit.Source is NuGetProvenance));

            var filtered = await service.FindReferencesAsync(
                "System.String", ApiReferenceKind.Base, exact: null, source: null, framework: null,
                limit: 1, cursor: null, cancellationToken: CancellationToken.None);
            Assert.HasCount(1, filtered.Hits);
            Assert.AreEqual(references.Totals, filtered.Totals);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
            if (Directory.Exists(reversedRoot))
                DeleteDirectory(reversedRoot);
        }
    }

    [TestMethod]
    public async Task MultiPackageOrderIsCanonicalForWinnersCoverageAndCursors()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var reversed = await CreateMultiPackageServiceAsync(
                root, reverseDefinitions: true, reverseStates: true);
            var reversedOrders = ReadMultiPackageOrders(root);
            CollectionAssert.AreEqual(ExpectedReversedPackageOrder, reversedOrders.Definitions);
            CollectionAssert.AreEqual(ExpectedReversedPackageOrder, reversedOrders.States);
            var lookup = await reversed.LookupAsync(
                "StringDerived", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.AreEqual("Fixture.Alpha", ((NuGetProvenance)lookup.Matches.Single().Source).PackageId);
            Assert.IsTrue(lookup.Matches.Single().Members.All(member =>
                ((NuGetProvenance)member.Source).PackageId == "Fixture.Alpha"));

            var text = await reversed.SearchTextAsync(
                "Needle", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.IsTrue(text.Hits.Where(hit => hit.Source is NuGetProvenance).All(hit =>
                ((NuGetProvenance)hit.Source).PackageId == "Fixture.Alpha"));
            CollectionAssert.AreEqual(
                ExpectedCanonicalPackageOrder,
                text.SearchedSources.OfType<NuGetProvenance>()
                    .Select(source => source.PackageId).ToArray());

            var first = await reversed.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: null,
                cancellationToken: CancellationToken.None);
            Assert.IsNotNull(first.NextPageToken);
            var reversedCoverage = first.SearchedSources.Select(source => source.RevisionKey).ToArray();

            var forward = await ReopenMultiPackageServiceAsync(
                root, reverseDefinitions: false, reverseStates: false);
            var forwardOrders = ReadMultiPackageOrders(root);
            CollectionAssert.AreEqual(ExpectedCanonicalPackageOrder, forwardOrders.Definitions);
            CollectionAssert.AreEqual(ExpectedCanonicalPackageOrder, forwardOrders.States);
            var forwardFirst = await forward.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: null,
                cancellationToken: CancellationToken.None);
            CollectionAssert.AreEqual(
                reversedCoverage,
                forwardFirst.SearchedSources.Select(source => source.RevisionKey).ToArray());
            Assert.AreEqual(first.Items.Single().Name, forwardFirst.Items.Single().Name);
            Assert.AreEqual(
                first.Items.Single().Source.RevisionKey,
                forwardFirst.Items.Single().Source.RevisionKey);
            var forwardLookup = await forward.LookupAsync(
                "StringDerived", "roslyn-api-docs", framework: null,
                limit: 100, cursor: null, cancellationToken: CancellationToken.None);
            Assert.AreEqual(
                lookup.Matches.Single().Source.RevisionKey,
                forwardLookup.Matches.Single().Source.RevisionKey);
            CollectionAssert.AreEqual(
                lookup.Matches.Single().Members.Select(member => member.Source.RevisionKey).ToArray(),
                forwardLookup.Matches.Single().Members.Select(member => member.Source.RevisionKey).ToArray());

            var continued = await forward.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: first.NextPageToken,
                cancellationToken: CancellationToken.None);
            Assert.IsNotEmpty(continued.Items);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task MultiPackageFrameworkDisagreementFailsClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMultiPackageServiceAsync(root, disagreeOnFrameworks: true);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.SearchAsync(
                "Widget", framework: null, limit: 20, cursor: null,
                cancellationToken: CancellationToken.None));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.SearchAsync(
                "Widget", framework: "net10.0", limit: 20, cursor: null,
                cancellationToken: CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task CursorsBindCanonicalFrameworkAndEveryParticipatingRevision()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);
            var initial = await service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: null,
                cancellationToken: CancellationToken.None);
            Assert.IsNotNull(initial.NextPageToken);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", "net8.0", limit: 1, cursor: initial.NextPageToken,
                cancellationToken: CancellationToken.None));

            var changedHash = Convert.ToBase64String(Enumerable.Repeat((byte)0x61, 64).ToArray());
            await UpdateMergedPackageRevisionAsync(root, "1.2.3", changedHash);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: initial.NextPageToken,
                cancellationToken: CancellationToken.None));

            var afterHash = await service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: null,
                cancellationToken: CancellationToken.None);
            await UpdateMergedPackageRevisionAsync(root, "1.2.4", changedHash);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: afterHash.NextPageToken,
                cancellationToken: CancellationToken.None));

            var afterVersion = await service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: null,
                cancellationToken: CancellationToken.None);
            await UpdateMergedGitRevisionAsync(root);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SearchAsync(
                "Widget", framework: null, limit: 1, cursor: afterVersion.NextPageToken,
                cancellationToken: CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task EmptyResultsRetainAllGitAndNuGetCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");

        try
        {
            var service = await CreateMergedServiceAsync(root);
            var lookup = await service.LookupAsync(
                "MissingType", source: null, framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            var search = await service.SearchAsync(
                "MissingType", framework: null, limit: 20, cursor: null,
                cancellationToken: CancellationToken.None);
            var text = await service.SearchTextAsync(
                "definitely absent prose", source: null, framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);
            var references = await service.FindReferencesAsync(
                "System.DefinitelyMissing", kind: null, exact: null, source: null, framework: null,
                limit: 20, cursor: null, cancellationToken: CancellationToken.None);

            AssertHonestMergedCoverage(lookup.SearchedSources, lookup.EffectiveFramework);
            AssertHonestMergedCoverage(search.SearchedSources, search.EffectiveFramework);
            AssertHonestMergedCoverage(text.SearchedSources, text.EffectiveFramework);
            AssertHonestMergedCoverage(references.SearchedSources, references.EffectiveFramework);
            Assert.AreEqual(ApiLookupOutcome.TypeNotFound, lookup.Outcome);
            Assert.IsEmpty(search.Items);
            Assert.IsEmpty(text.Hits);
            Assert.IsEmpty(references.Hits);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static void AssertFrameworkCoverage(
        string expectedEffective,
        string? effective,
        string? defaultFramework,
        IReadOnlyList<string>? availableFrameworks)
    {
        Assert.AreEqual(expectedEffective, effective);
        Assert.AreEqual("net10.0", defaultFramework);
        CollectionAssert.AreEqual(PackageFrameworks, availableFrameworks!.ToArray());
    }

    private static void AssertHonestMergedCoverage(
        IReadOnlyList<ApiProvenance> searchedSources,
        string? effectiveFramework)
    {
        Assert.HasCount(3, searchedSources);
        Assert.HasCount(2, searchedSources.OfType<GitProvenance>());
        Assert.HasCount(1, searchedSources.OfType<NuGetProvenance>());
        Assert.AreEqual("net10.0", effectiveFramework);
    }
}
