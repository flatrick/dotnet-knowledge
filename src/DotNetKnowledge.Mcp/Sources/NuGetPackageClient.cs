using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotNetKnowledge.Mcp.Sources;

public sealed record NuGetPackageDownload(string Sha512, DateTimeOffset FetchedAt);

public interface INuGetPackageClient
{
    Task<NuGetPackageDownload> DownloadAsync(
        ApiPackageDefinition package,
        string version,
        string? expectedSha512,
        string destination,
        CancellationToken cancellationToken);
}

public sealed class NuGetPackageClient(HttpClient httpClient) : INuGetPackageClient
{
    private const int MaximumRedirects = 5;

    public async Task<NuGetPackageDownload> DownloadAsync(
        ApiPackageDefinition package,
        string version,
        string? expectedSha512,
        string destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var packageBaseAddress = await GetPackageBaseAddressAsync(package.Feed, cancellationToken)
            .ConfigureAwait(false);
        var lowercaseId = package.PackageId.ToLowerInvariant();
        var lowercaseVersion = version.ToLowerInvariant();
        var packageAddress = new Uri(
            packageBaseAddress,
            $"{lowercaseId}/{lowercaseVersion}/{lowercaseId}.{lowercaseVersion}.nupkg");
        var hashAddress = new Uri($"{packageAddress.AbsoluteUri}.sha512", UriKind.Absolute);

        byte[] serverHash;
        using (var hashResponse = await SendAsync(hashAddress, cancellationToken).ConfigureAwait(false))
        {
            var encodedServerHash = await hashResponse.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            serverHash = DecodeSha512(encodedServerHash, "NuGet package hash");
        }

        if (expectedSha512 is not null)
        {
            var catalogHash = DecodeSha512(expectedSha512, "catalog package hash");
            if (!CryptographicOperations.FixedTimeEquals(serverHash, catalogHash))
                throw new InvalidDataException("The NuGet package hash does not match the catalog hash.");
        }

        var fullDestination = Path.GetFullPath(destination);
        var destinationDirectory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidDataException("The package destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            byte[] downloadedHash;
            using (var packageResponse = await SendAsync(packageAddress, cancellationToken).ConfigureAwait(false))
            await using (var packageStream = await packageResponse.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false))
            await using (var temporaryFile = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA512))
            {
                var buffer = new byte[128 * 1024];
                int count;
                while ((count = await packageStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    hasher.AppendData(buffer, 0, count);
                    await temporaryFile.WriteAsync(buffer.AsMemory(0, count), cancellationToken)
                        .ConfigureAwait(false);
                }

                await temporaryFile.FlushAsync(cancellationToken).ConfigureAwait(false);
                downloadedHash = hasher.GetHashAndReset();
            }

            if (!CryptographicOperations.FixedTimeEquals(serverHash, downloadedHash))
                throw new InvalidDataException("The downloaded NuGet package does not match its server hash.");

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullDestination, overwrite: true);
            return new NuGetPackageDownload(
                Convert.ToBase64String(downloadedHash),
                DateTimeOffset.UtcNow);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    private async Task<Uri> GetPackageBaseAddressAsync(string feed, CancellationToken cancellationToken)
    {
        var feedAddress = RequireHttpsAbsoluteUri(feed, "NuGet feed");
        using var response = await SendAsync(feedAddress, cancellationToken).ConfigureAwait(false);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("resources", out var resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The NuGet service index has no resources array.");
        }

        var packageBaseAddresses = new List<Uri>();
        foreach (var resource in resources.EnumerateArray())
        {
            if (!IsPackageBaseAddress(resource)
                || !resource.TryGetProperty("@id", out var id)
                || id.ValueKind != JsonValueKind.String
                || !Uri.TryCreate(id.GetString(), UriKind.Absolute, out var address)
                || address.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            packageBaseAddresses.Add(new Uri($"{address.AbsoluteUri.TrimEnd('/')}/", UriKind.Absolute));
        }

        if (packageBaseAddresses.Count != 1)
        {
            throw new InvalidDataException(
                "The NuGet service index must contain exactly one HTTPS PackageBaseAddress/3.0.0 resource.");
        }

        return packageBaseAddresses[0];
    }

    private async Task<HttpResponseMessage> SendAsync(Uri address, CancellationToken cancellationToken)
    {
        var currentAddress = RequireHttpsAbsoluteUri(address.AbsoluteUri, "NuGet request");
        for (var redirectCount = 0; ;)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentAddress);
            var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                try
                {
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new InvalidDataException("The NuGet redirect has no Location header.");
            if (redirectCount == MaximumRedirects)
                throw new InvalidDataException($"The NuGet request exceeded {MaximumRedirects} redirects.");

            var redirectedAddress = location.IsAbsoluteUri ? location : new Uri(currentAddress, location);
            currentAddress = RequireHttpsAbsoluteUri(redirectedAddress.AbsoluteUri, "NuGet redirect");
            redirectCount++;
        }
    }

    private static bool IsPackageBaseAddress(JsonElement resource)
    {
        if (!resource.TryGetProperty("@type", out var type))
            return false;

        return type.ValueKind switch
        {
            JsonValueKind.String => IsPackageBaseAddressType(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String && IsPackageBaseAddressType(item.GetString())),
            _ => false,
        };
    }

    private static bool IsPackageBaseAddressType(string? type) =>
        string.Equals(type, "PackageBaseAddress/3.0.0", StringComparison.Ordinal);

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Redirect
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static Uri RequireHttpsAbsoluteUri(string value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"The {description} must be an absolute HTTPS URI.");
        }

        return address;
    }

    private static byte[] DecodeSha512(string encodedHash, string description)
    {
        byte[] hash;
        try
        {
            hash = Convert.FromBase64String(encodedHash.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The {description} is not valid base64.", exception);
        }

        if (hash.Length != 64)
            throw new InvalidDataException($"The {description} is not a 64-byte SHA-512 hash.");

        return hash;
    }
}
