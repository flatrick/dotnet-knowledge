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
            var repository = Path.Combine(root, "origin");
            var namespaceDirectory = Path.Combine(repository, "xml", "System");
            Directory.CreateDirectory(namespaceDirectory);
            await RunGitAsync(null, "init", "--initial-branch=main", repository);
            await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
            await RunGitAsync(repository, "config", "user.name", "Tests");
            await File.WriteAllTextAsync(Path.Combine(namespaceDirectory, "Widget.xml"), """
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
                  </Members>
                </Type>
                """);
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
                "Widget.Create",
                "dotnet-api-docs",
                CancellationToken.None);

            Assert.HasCount(1, result.Matches);
            var match = result.Matches[0];
            Assert.AreEqual("System.Widget", match.FullName);
            Assert.HasCount(1, match.Members);
            var member = match.Members[0];
            Assert.AreEqual("public static System.Widget Create(string name);", member.Signature);
            Assert.AreEqual("Creates a widget.", member.Summary);
            Assert.AreEqual("The widget name.", member.Parameters[0].Description);
            Assert.AreEqual("The new widget.", member.Returns);
            Assert.AreEqual("Names are case-sensitive.", member.Remarks);
            Assert.AreEqual("test/dotnet-api-docs", match.Source.Repo);
            Assert.AreEqual("pinned", match.Source.Ref);
            Assert.AreEqual(pin, match.Source.Commit);

            var missing = await service.LookupAsync(
                "System.MissingWidget",
                "dotnet-api-docs",
                CancellationToken.None);
            Assert.IsEmpty(missing.Matches);
            Assert.HasCount(1, missing.SearchedSources);
            Assert.AreEqual(pin, missing.SearchedSources[0].Commit);

            foreach (var maliciousSymbol in new[] { "../Widget", "..\\Widget", "System.*", "C:\\Widget" })
            {
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.LookupAsync(
                    maliciousSymbol,
                    "dotnet-api-docs",
                    CancellationToken.None));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
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

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }
}
