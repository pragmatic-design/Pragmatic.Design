using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the <see cref="ISignedUrlProvider" /> capability of <see cref="GoogleCloudFileStorage" />.
///     Signing requires a service-account credential: with neither an explicit
///     <see cref="GoogleCloudStorageOptions.UrlSigner" /> nor <c>GOOGLE_APPLICATION_CREDENTIALS</c>
///     configured, the request fails fast with <see cref="NotSupportedException" />.
/// </summary>
/// <remarks>
///     The successful signing path is not exercised here: producing a real signed URL requires a
///     service-account private key, which is unavailable offline. It is covered by the E2E suite
///     against a real credential.
/// </remarks>
public sealed class GoogleCloudFileStoragePreSignedUrlTests
{
    private readonly StorageClientMock _gcs = new StorageClientMock();

    private GoogleCloudFileStorage CreateStorage()
        => new(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);

    [Fact]
    public async Task GetDownloadUrlAsync_NoSignerConfigured_ThrowsNotSupported()
    {
        var previous = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", null);
        try
        {
            var storage = CreateStorage();

            var act = () => storage.GetDownloadUrlAsync(new Uri("gs://bkt/photos/x.jpg"), TimeSpan.FromMinutes(15));

            await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*service-account credentials*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", previous);
        }
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ForeignUri_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.GetDownloadUrlAsync(
            new Uri("https://evil.example.com/photos/x.jpg"), TimeSpan.FromMinutes(15));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
