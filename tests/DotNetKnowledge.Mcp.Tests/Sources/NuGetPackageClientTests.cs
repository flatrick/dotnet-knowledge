using System.Net;
using System.Security.Cryptography;
using System.Text;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Sources;

[TestClass]
public sealed class NuGetPackageClientTests
{
    private const int ExpectedMaximumServiceIndexBytes = 1024 * 1024;
    private const int ExpectedMaximumPackageBytes = 64 * 1024 * 1024;

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
                "https://flat.test/v3-flatcontainer/test.package/5.3.0-beta/test.package.5.3.0-beta.nupkg",
            };
            CollectionAssert.AreEqual(expectedUris, requestedUris.Select(uri => uri.AbsoluteUri).ToList());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The flat container defines three resources: {id}/index.json, the .nupkg and the .nuspec. A
    // feed that serves only those is not a degraded feed, it is every real one, so a download that
    // needs anything else cannot succeed anywhere. This stub answers 404 exactly as nuget.org does.
    [TestMethod]
    public async Task DownloadSucceedsAgainstAFeedServingOnlyTheDefinedFlatContainerResources()
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
                    "https://flat.test/v3-flatcontainer/test.package/5.3.0/test.package.5.3.0.nupkg" =>
                        BytesResponse(packageBytes),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                });
            }));
            var destination = Path.Combine(root, "package.nupkg");

            var result = await new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", expectedSha512, destination, CancellationToken.None);

            Assert.AreEqual(expectedSha512, result.Sha512);
            CollectionAssert.AreEqual(packageBytes, await File.ReadAllBytesAsync(destination));
            var expectedUris = new List<string>
            {
                "https://feed.test/v3/index.json",
                "https://flat.test/v3-flatcontainer/test.package/5.3.0/test.package.5.3.0.nupkg",
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

    [TestMethod]
    [DataRow("//evil.test/package")]
    [DataRow("../escape")]
    [DataRow("package/name")]
    [DataRow("package?query")]
    [DataRow("package#fragment")]
    public async Task DownloadRejectsPackageIdsThatAreNotExactNuGetPathSegments(string packageId)
    {
        var requests = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(JsonResponse(ServiceIndex()));
        }));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new NuGetPackageClient(httpClient).DownloadAsync(
                Package() with { PackageId = packageId },
                "5.3.0",
                Package().Sha512,
                Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.nupkg"),
                CancellationToken.None));

        Assert.AreEqual(0, requests, "Invalid package identity reached the network boundary.");
    }

    [TestMethod]
    [DataRow("//evil.test/version")]
    [DataRow("../5.3.0")]
    [DataRow("5.3.0/path")]
    [DataRow("5.3.0?query")]
    [DataRow("5.3.0#fragment")]
    [DataRow("latest")]
    public async Task DownloadRejectsVersionsThatAreNotExactNuGetVersionSegments(string version)
    {
        var requests = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(JsonResponse(ServiceIndex()));
        }));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new NuGetPackageClient(httpClient).DownloadAsync(
                Package(),
                version,
                Package().Sha512,
                Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.nupkg"),
                CancellationToken.None));

        Assert.AreEqual(0, requests, "Invalid package version reached the network boundary.");
    }

    [TestMethod]
    [DataRow("https://flat.test/v3-flatcontainer/?alternate=evil")]
    [DataRow("https://flat.test/v3-flatcontainer/#fragment")]
    public async Task DownloadRejectsPackageBaseAddressesWithQueryOrFragment(string packageBaseAddress)
    {
        var requests = 0;
        var serviceIndex = $$"""
            {"version":"3.0.0","resources":[
              {"@id":"{{packageBaseAddress}}","@type":"PackageBaseAddress/3.0.0"}
            ]}
            """;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(JsonResponse(serviceIndex));
        }));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", Package().Sha512,
                Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.nupkg"),
                CancellationToken.None));

        Assert.AreEqual(1, requests, "An unusable package base address was used for an asset request.");
    }

    [TestMethod]
    public async Task DownloadSucceedsAfterExactlyFiveHttpsRedirects()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("five redirects");
            var hash = Convert.ToBase64String(SHA512.HashData(packageBytes));
            var serviceRequests = 0;
            using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.Host == "feed.test")
                {
                    serviceRequests++;
                    return Task.FromResult(serviceRequests <= 5
                        ? RedirectResponse($"https://feed.test/redirect/{serviceRequests}")
                        : JsonResponse(ServiceIndex()));
                }

                return Task.FromResult(request.RequestUri.AbsolutePath.EndsWith(".sha512", StringComparison.Ordinal)
                    ? TextResponse(hash)
                    : BytesResponse(packageBytes));
            }));

            await new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", hash, Path.Combine(root, "package.nupkg"), CancellationToken.None);

            Assert.AreEqual(6, serviceRequests);
            Assert.IsTrue(File.Exists(Path.Combine(root, "package.nupkg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadAcceptsAServiceIndexAtTheByteLimit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var packageBytes = Encoding.UTF8.GetBytes("bounded index");
            var hash = Convert.ToBase64String(SHA512.HashData(packageBytes));
            var serviceIndex = PadWithWhitespace(ServiceIndex(), ExpectedMaximumServiceIndexBytes);
            using var httpClient = CreateSuccessfulHttpClient(packageBytes, hash, serviceIndex, hash);

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
    public async Task DownloadRejectsAServiceIndexOverTheByteLimit()
    {
        var serviceIndex = PadWithWhitespace(ServiceIndex(), ExpectedMaximumServiceIndexBytes + 1);
        var packageBytes = Encoding.UTF8.GetBytes("over-limit service index");
        var hash = Convert.ToBase64String(SHA512.HashData(packageBytes));
        using var httpClient = CreateSuccessfulHttpClient(packageBytes, hash, serviceIndex, hash);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", hash,
                Path.Combine(Path.GetTempPath(), $"unused-{Guid.NewGuid():N}.nupkg"),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task DownloadAcceptsPackageContentAtTheCompressedByteLimit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var hash = HashZeroBytes(ExpectedMaximumPackageBytes);
            using var httpClient = CreateStreamingHttpClient(ExpectedMaximumPackageBytes, hash);
            var destination = Path.Combine(root, "package.nupkg");

            await new NuGetPackageClient(httpClient).DownloadAsync(
                Package(), "5.3.0", hash, destination, CancellationToken.None);

            Assert.AreEqual(ExpectedMaximumPackageBytes, new FileInfo(destination).Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task DownloadRejectsStreamedPackageContentOverTheCompressedByteLimit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var hash = HashZeroBytes(ExpectedMaximumPackageBytes + 1L);
            using var httpClient = CreateStreamingHttpClient(ExpectedMaximumPackageBytes + 1L, hash);
            var destination = Path.Combine(root, "package.nupkg");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", hash, destination, CancellationToken.None));

            Assert.IsFalse(File.Exists(destination));
            Assert.HasCount(0, Directory.EnumerateFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancellationDuringPackageStreamingRemovesTempAndPreservesExistingDestination()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(root, "package.nupkg");
            var originalBytes = Encoding.UTF8.GetBytes("previous verified package");
            await File.WriteAllBytesAsync(destination, originalBytes);
            var hash = Convert.ToBase64String(new byte[64]);
            using var cancellation = new CancellationTokenSource();
            using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsolutePath switch
                {
                    "/v3/index.json" => JsonResponse(ServiceIndex()),
                    var path when path.EndsWith(".sha512", StringComparison.Ordinal) => TextResponse(hash),
                    var path when path.EndsWith(".nupkg", StringComparison.Ordinal) =>
                        StreamResponse(new CancellingStream(cancellation)),
                    _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
                })));

            try
            {
                await new NuGetPackageClient(httpClient).DownloadAsync(
                    Package(), "5.3.0", hash, destination, cancellation.Token);
                Assert.Fail("The package stream did not cancel the download.");
            }
            catch (OperationCanceledException)
            {
            }

            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(destination));
            var expectedFiles = new List<string> { destination };
            CollectionAssert.AreEqual(expectedFiles, Directory.EnumerateFiles(root).ToList());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpClient CreateSuccessfulHttpClient(
        byte[] packageBytes,
        string serverSha512,
        string? serviceIndex = null,
        string? hashResponse = null) =>
        new(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => JsonResponse(serviceIndex ?? ServiceIndex()),
                var path when path.EndsWith(".nupkg.sha512", StringComparison.Ordinal) =>
                    TextResponse(hashResponse ?? serverSha512),
                var path when path.EndsWith(".nupkg", StringComparison.Ordinal) => BytesResponse(packageBytes),
                _ => throw new AssertFailedException($"Unexpected request: {request.RequestUri}"),
            })));

    private static HttpClient CreateStreamingHttpClient(long packageLength, string serverSha512) =>
        new(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => JsonResponse(ServiceIndex()),
                var path when path.EndsWith(".nupkg.sha512", StringComparison.Ordinal) => TextResponse(serverSha512),
                var path when path.EndsWith(".nupkg", StringComparison.Ordinal) =>
                    StreamResponse(new GeneratedZeroStream(packageLength)),
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

    private static HttpResponseMessage StreamResponse(Stream stream) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(stream),
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

    private static string PadWithWhitespace(string value, int byteCount)
    {
        var existingBytes = Encoding.UTF8.GetByteCount(value);
        Assert.IsTrue(byteCount >= existingBytes);
        return value + new string(' ', byteCount - existingBytes);
    }

    private static string HashZeroBytes(long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[128 * 1024];
        for (long remaining = length; remaining > 0;)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }

        return Convert.ToBase64String(hash.GetHashAndReset());
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class GeneratedZeroStream(long length) : Stream
    {
        private readonly long _length = length;
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _length - _remaining;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = (int)Math.Min(count, _remaining);
            Array.Clear(buffer, offset, bytesRead);
            _remaining -= bytesRead;
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesRead = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..bytesRead].Clear();
            _remaining -= bytesRead;
            return ValueTask.FromResult(bytesRead);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : Stream
    {
        private bool _hasRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_hasRead)
                return ValueTask.FromCanceled<int>(cancellationToken);

            _hasRead = true;
            buffer.Span[..Math.Min(16, buffer.Length)].Fill(42);
            cancellation.Cancel();
            return ValueTask.FromResult(Math.Min(16, buffer.Length));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
