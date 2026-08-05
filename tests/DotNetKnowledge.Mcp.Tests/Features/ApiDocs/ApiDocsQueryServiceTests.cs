using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

[TestClass]
public sealed class ApiDocsQueryServiceTests
{
    private static readonly string[] ExpectedFirstPageNames = ["System.AlphaWidget", "System.BetaWidget"];
    private static readonly string[] ExpectedSecondPageNames = ["System.GammaWidget"];
    private static readonly string[] ExpectedResolvedTypeNames = ["System.Widget"];
    private static readonly string[] ExpectedHolderTypes = ["System.Holder", "System.Holder<T>"];

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

    private const string WidgetXml = """
        <Type Name="Widget" FullName="System.Widget">
          <Members>
            <Member MemberName="Create">
              <MemberSignature Language="C#" Value="public static System.Widget Create(string name);" />
              <Parameters><Parameter Name="name" Type="System.String" /></Parameters>
              <ReturnValue><ReturnType>System.Widget</ReturnType></ReturnValue>
              <Docs>
                <summary>Creates a widget.</summary>
                <param name="name">The widget name.</param>
                <returns>The new widget.</returns>
                <remarks>Names are case-sensitive.</remarks>
              </Docs>
            </Member>
            <Member MemberName="Convert&lt;TResult&gt;">
              <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult&gt;();" />
              <Docs><summary>Converts to one type.</summary></Docs>
            </Member>
            <Member MemberName="Convert&lt;TResult,TState&gt;">
              <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult,TState&gt;(TState state);" />
              <Docs><summary>Converts with state.</summary></Docs>
            </Member>
          </Members>
        </Type>
        """;

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
            Assert.HasCount(3, wholeType.Matches[0].Members);
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
            Assert.HasCount(1, secondPage.Matches[0].Members);
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

    private static async Task<ApiDocsQueryService> CreateWidgetServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var namespaceDirectory = Path.Combine(repository, "xml", "System");
        Directory.CreateDirectory(namespaceDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(namespaceDirectory, "Widget.xml"), WidgetXml);

        // A second type whose name also contains "Widget" so a search for that pattern has more
        // than one match. lookup_api's exact-name resolution never matches this file, so it is
        // invisible to every lookup test that shares this fixture; it exists purely so
        // search_api("Widget", limit: 1) has a real page boundary to hand a cursor across.
        await File.WriteAllTextAsync(
            Path.Combine(namespaceDirectory, "WidgetKit.xml"),
            "<Type Name=\"WidgetKit\" FullName=\"System.WidgetKit\" />");
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
        return new ApiDocsQueryService(catalog, cache, synchronizer);
    }

    private static async Task WriteCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["dotnet-api-docs"] = new
                {
                    repository = "test/dotnet-api-docs",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "xml" },
                    purpose = "Test API docs.",
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> RunGitAsync(string? workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
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

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }
}
