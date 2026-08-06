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
    private static readonly string[] ExpectedStringParameterTypes =
    [
        "System.String",
        "System.String[]",
        "System.Collections.Generic.IEnumerable<System.String>",
    ];

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
            Assert.AreEqual("test/dotnet-api-docs", first.Items[0].Source.Repo);
            Assert.HasCount(1, first.SearchedSources);
            Assert.AreEqual(pin, first.SearchedSources[0].Commit);

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
            Assert.AreEqual("test/dotnet-api-docs", match.Source.Repo);
            Assert.AreEqual("pinned", match.Source.Ref);
            Assert.AreEqual(pin, match.Source.Commit);

            var missing = await service.LookupAsync(
                "System.MissingWidget",
                "dotnet-api-docs",
                limit: 20,
                cursor: null,
                CancellationToken.None);
            Assert.IsEmpty(missing.Matches);
            Assert.HasCount(1, missing.SearchedSources);
            Assert.AreEqual(pin, missing.SearchedSources[0].Commit);

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

            // A namespace names everything it holds, and says that is what it did.
            var byNamespace = await Search(service, "System");
            CollectionAssert.AreEqual(ExpectedWidgetTypes, byNamespace.Select(item => item.Name).ToArray());
            Assert.IsTrue(byNamespace.All(item => item.MatchedOn == ApiNameMatch.Namespace));

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
            Assert.AreEqual("test/dotnet-api-docs", bySummary[0].Source.Repo);

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

            // Totals name every kind, so a caller filtering to one still sees the others exist.
            var totals = (await service.FindReferencesAsync(
                "System.WidgetMarker", null, null, "dotnet-api-docs", 100, null, CancellationToken.None))
                .Totals;
            Assert.AreEqual(2, totals.Attribute);
            Assert.AreEqual(0, totals.Constraint);
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
                "<Type Name=\"Holder\" FullName=\"System.Holder\" />");
            await File.WriteAllTextAsync(
                Path.Combine(namespaceDirectory, "Holder`1.xml"),
                "<Type Name=\"Holder`1\" FullName=\"System.Holder&lt;T&gt;\" />");
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
}
