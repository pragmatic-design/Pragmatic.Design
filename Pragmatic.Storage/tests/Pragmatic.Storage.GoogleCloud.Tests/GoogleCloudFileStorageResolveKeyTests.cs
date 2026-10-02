using Google.Apis.Download;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the URI→object-name resolution of <see cref="GoogleCloudFileStorage" />: only
///     <c>gs://</c> URIs whose bucket matches the configured bucket, and URIs prefixed with the
///     configured <c>PublicBaseUrl</c>, are accepted; anything else is rejected with
///     <see cref="ArgumentException" /> to prevent arbitrary object-name injection.
/// </summary>
public sealed class GoogleCloudFileStorageResolveKeyTests
{
    private readonly StorageClientMock _gcs = new StorageClientMock();

    private GoogleCloudFileStorage CreateStorage(string? publicBaseUrl = null)
        => new(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt", PublicBaseUrl = publicBaseUrl },
            NullLogger<GoogleCloudFileStorage>.Instance);

    private void SetupDownload(byte[] payload)
        => _gcs.DownloadObjectAsync6Setup.Returns(args =>
        {
            ((Stream)args[2]!).Write(payload, 0, payload.Length);
            return Task.FromResult(new StorageObject());
        });

    [Fact]
    public async Task GetAsync_GsSchemeUri_ResolvesNameAndReturnsStream()
    {
        SetupDownload([1, 2, 3]);
        var storage = CreateStorage();

        var stream = await storage.GetAsync(new Uri("gs://bkt/uploads/photos/x.jpg"));

        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        ms.ToArray().Should().Equal(1, 2, 3);
        _gcs.DownloadObjectAsync6Setup.Received(1, args => (string)args[0]! == "bkt" && (string)args[1]! == "uploads/photos/x.jpg");
    }

    [Fact]
    public async Task GetAsync_GsSchemeBucketMismatch_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.GetAsync(new Uri("gs://other-bucket/photos/x.jpg"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }

    [Fact]
    public async Task GetAsync_PublicBaseUrlUri_StripsPrefixToResolveName()
    {
        SetupDownload([7]);
        var storage = CreateStorage("https://cdn.example.com");

        var stream = await storage.GetAsync(new Uri("https://cdn.example.com/photos/x.jpg"));

        stream.Should().NotBeNull();
        _gcs.DownloadObjectAsync6Setup.Received(1, args => (string)args[0]! == "bkt" && (string)args[1]! == "photos/x.jpg");
    }

    [Fact]
    public async Task GetAsync_ForeignUri_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.GetAsync(new Uri("https://evil.example.com/photos/x.jpg"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }

    [Fact]
    public async Task GetAsync_UriNotMatchingPublicBaseUrl_ThrowsArgumentException()
    {
        var storage = CreateStorage("https://cdn.example.com");

        var act = () => storage.GetAsync(new Uri("https://other.example.com/photos/x.jpg"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExistsAsync_ForeignUri_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.ExistsAsync(new Uri("https://evil.example.com/photos/x.jpg"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsync_ForeignUri_ThrowsArgumentException()
    {
        var storage = CreateStorage();

        var act = () => storage.DeleteAsync(new Uri("https://evil.example.com/photos/x.jpg"));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
