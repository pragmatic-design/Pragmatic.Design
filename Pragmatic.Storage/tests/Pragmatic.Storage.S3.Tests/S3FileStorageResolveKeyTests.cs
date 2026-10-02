using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the URI→key resolution of <see cref="S3FileStorage" />: only <c>s3://</c> URIs and
///     URIs prefixed with the configured <c>PublicBaseUrl</c> are accepted; anything else is
///     rejected with <see cref="ArgumentException" /> to prevent arbitrary key injection.
/// </summary>
public sealed class S3FileStorageResolveKeyTests
{
    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();

    private S3FileStorage CreateStorage(string? publicBaseUrl = null)
        => new(_s3, new S3StorageOptions { BucketName = "bkt", PublicBaseUrl = publicBaseUrl },
            NullLogger<S3FileStorage>.Instance);

    [Fact]
    public async Task GetAsync_S3SchemeUri_ResolvesKeyAndReturnsStream()
    {
        _s3.GetObjectAsync3.When("bkt", "uploads/photos/x.jpg", Arg.Any<CancellationToken>())
.Returns(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        var storage = CreateStorage();

        var stream = await storage.GetAsync(new Uri("s3://bkt/uploads/photos/x.jpg"));

        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        ms.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task GetAsync_PublicBaseUrlUri_StripsPrefixToResolveKey()
    {
        _s3.GetObjectAsync3.When("bkt", "photos/x.jpg", Arg.Any<CancellationToken>())
.Returns(new GetObjectResponse { ResponseStream = new MemoryStream([7]) });
        var storage = CreateStorage("https://cdn.example.com");

        var stream = await storage.GetAsync(new Uri("https://cdn.example.com/photos/x.jpg"));

        stream.Should().NotBeNull();
        _s3.GetObjectAsync3.Received(1, "bkt", "photos/x.jpg", Arg.Any<CancellationToken>());
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
