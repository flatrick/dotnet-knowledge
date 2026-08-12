using System.Net;
using System.Security.Cryptography;
using System.Text;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class NuGetPackageClientTests
{
    [TestMethod]
    public async Task DownloadUsesLowercaseFlatContainerUrlsAndPublishesVerifiedBytes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("repository-authored package fixture");
            var expectedSha512 = Convert.ToBase64String(SHA512.HashData(packageBytes));
            var requestedUris = new List<Uri>();
            using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                requestedUris.Add(request.RequestUri!);
                return Task.FromResult(request.RequestUri!.AbsoluteUri switch
                {
                    "https://feed.test/v3/index.json" => JsonResponse(ServiceIndex()),
                    "https://flat.test/v3-flatcontainer/test.package/5.3.0-beta/test.package.5.3.0-beta.nupkg.sha512" =>
                        TextResponse(expectedSha512),
                    "https://flat.test/v3-flatcontainer/test.package/5.3.0-beta/test.package.5.3.0-beta.nupkg" =>
                        BytesResponse(packageBytes),
                    _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
                });
            }));
            var client = new NuGetPackageClient(httpClient);
            var destination = Path.Combine(root, "package.nupkg");

            var result = await client.DownloadAsync(
                Package(), "5.3.0-BETA", expectedSha512, destination, CancellationToken.None);

            Assert.AreEqual(expectedSha512, result.Sha512);
            Assert.IsTrue(result.FetchedAt <= DateTimeOffset.UtcNow);
            CollectionAssert.AreEqual(packageBytes, await File.ReadAllBytesAsync(destination));
            var expectedUris = new List<string>
            {
                "https://feed.test/v3/index.json",
                "https://flat.test/v3-flatcontainer/test.package/5.3.0-beta/test.package.5.3.0-beta.nupkg.sha512",
                "https://flat.test/v3-flatcontainer/test.package/5.3.0-beta/test.package.5.3.0-beta.nupkg",
            };
            CollectionAssert.AreEqual(expectedUris, requestedUris.Select(uri => uri.AbsoluteUri).ToList());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadAcceptsTheServerHashWhenNoCatalogHashApplies()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("head package fixture");
            var serverSha512 = Convert.ToBase64String(SHA512.HashData(packageBytes));
            using var httpClient = CreateSuccessfulHttpClient(packageBytes, serverSha512);
            var destination = Path.Combine(root, "package.nupkg");

            var result = await new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "6.0.0", null, destination, CancellationToken.None);

            Assert.AreEqual(serverSha512, result.Sha512);
            CollectionAssert.AreEqual(packageBytes, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRejectsCatalogAndServerHashMismatchWithoutPublishingAFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("package fixture");
            var serverSha512 = Convert.ToBase64String(SHA512.HashData(packageBytes));
            var catalogSha512 = Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes("different")));
            using var httpClient = CreateSuccessfulHttpClient(packageBytes, serverSha512);
            var destination = Path.Combine(root, "package.nupkg");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", catalogSha512, destination, CancellationToken.None));

            Assert.IsFalse(File.Exists(destination));
            Assert.HasCount(0, Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRejectsPackageContentHashMismatchWithoutPublishingAFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var advertisedBytes = Encoding.UTF8.GetBytes("advertised package");
            var downloadedBytes = Encoding.UTF8.GetBytes("different package");
            var serverSha512 = Convert.ToBase64String(SHA512.HashData(advertisedBytes));
            using var httpClient = CreateSuccessfulHttpClient(downloadedBytes, serverSha512);
            var destination = Path.Combine(root, "package.nupkg");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", serverSha512, destination, CancellationToken.None));

            Assert.IsFalse(File.Exists(destination));
            Assert.HasCount(0, Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadCancellationLeavesNoDestinationOrTemporaryFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("The cancelled request unexpectedly completed.");
            }));
            var destination = Path.Combine(root, "package.nupkg");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                await new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", Package().Sha512, destination, cancellation.Token);
                Assert.Fail("The download did not observe cancellation.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsFalse(File.Exists(destination));
            Assert.HasCount(0, Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadFollowsHttpsRedirects()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("redirected package");
            var hash = Convert.ToBase64String(SHA512.HashData(packageBytes));
            using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsoluteUri switch
                {
                    "https://feed.test/v3/index.json" => RedirectResponse("https://feed.test/v3/redirected.json"),
                    "https://feed.test/v3/redirected.json" => JsonResponse(ServiceIndex()),
                    "https://flat.test/v3-flatcontainer/test.package/5.3.0/test.package.5.3.0.nupkg.sha512" => TextResponse(hash),
                    "https://flat.test/v3-flatcontainer/test.package/5.3.0/test.package.5.3.0.nupkg" => BytesResponse(packageBytes),
                    _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
                })));

            await new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", hash, Path.Combine(root, "package.nupkg"), CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(root, "package.nupkg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRejectsRedirectsToHttp()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(RedirectResponse("http://feed.test/insecure.json"))));
            var destination = Path.Combine(root, "package.nupkg");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", Package().Sha512, destination, CancellationToken.None));

            Assert.IsFalse(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRejectsMoreThanFiveRedirects()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var redirect = 0;
            using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(RedirectResponse($"https://feed.test/redirect/{++redirect}"))));
            var destination = Path.Combine(root, "package.nupkg");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", Package().Sha512, destination, CancellationToken.None));

            Assert.AreEqual(6, redirect);
            Assert.IsFalse(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRequiresExactlyOneHttpsPackageBaseAddress()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const string serviceIndex = """
                {"version":"3.0.0","resources":[
                  {"@id":"https://flat.test/one/","@type":"PackageBaseAddress/3.0.0"},
                  {"@id":"https://flat.test/two/","@type":"PackageBaseAddress/3.0.0"}
                ]}
                """;
            using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(JsonResponse(serviceIndex))));

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", Package().Sha512,
                    Path.Combine(root, "package.nupkg"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpClient CreateSuccessfulHttpClient(byte[] packageBytes, string serverSha512) =>
        new(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => JsonResponse(ServiceIndex()),
                var path when path.EndsWith(".nupkg.sha512", StringComparison.Ordinal) => TextResponse(serverSha512),
                var path when path.EndsWith(".nupkg", StringComparison.Ordinal) => BytesResponse(packageBytes),
                _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
            })));

    private static ApiPackageDefinition Package() => new(
        "Test.Package",
        "Test.Assembly",
        "https://feed.test/v3/index.json",
        "5.3.0",
        Convert.ToBase64String(new byte[64]),
        "net10.0");

    private static string ServiceIndex() => """
        {"version":"3.0.0","resources":[
          {"@id":"https://flat.test/v3-flatcontainer/","@type":"PackageBaseAddress/3.0.0"}
        ]}
        """;

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.ASCII, "text/plain"),
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private static HttpResponseMessage RedirectResponse(string location) => new(HttpStatusCode.Redirect)
    {
        Headers = { Location = new Uri(location) },
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
