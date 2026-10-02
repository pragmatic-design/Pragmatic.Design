using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the <see cref="ISignedUrlProvider" /> capability of <see cref="S3FileStorage" />:
///     <see cref="S3FileStorage.GetDownloadUrlAsync" /> issues a GET pre-signed request with the
///     resolved key and the expected expiry, and returns the SDK-produced URL.
/// </summary>
public sealed class S3FileStoragePreSignedUrlTests
{
    private const string SignedUrl = "https://bkt.s3.amazonaws.com/photos/x.jpg?X-Amz-Signature=deadbeef";

    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();
    private GetPreSignedUrlRequest? _captured;
    private readonly S3FileStorage _storage;

    public S3FileStoragePreSignedUrlTests()
    {
        _s3.GetPreSignedURLAsync.When(Arg.Do<GetPreSignedUrlRequest>(r => _captured = r))
.Returns(SignedUrl);
        _storage = new S3FileStorage(_s3, new S3StorageOptions { BucketName = "bkt", KeyPrefix = "uploads/" },
            NullLogger<S3FileStorage>.Instance);
    }

    [Fact]
    public async Task GetDownloadUrlAsync_S3SchemeUri_RequestsGetVerbWithResolvedKeyAndExpiry()
    {
        var expiry = TimeSpan.FromMinutes(15);
        var expectedExpiry = DateTime.UtcNow + expiry;

        var url = await _storage.GetDownloadUrlAsync(new Uri("s3://bkt/uploads/photos/x.jpg"), expiry);

        url.ToString().Should().Be(SignedUrl);
        _captured.Should().NotBeNull();
        _captured!.BucketName.Should().Be("bkt");
        _captured.Key.Should().Be("uploads/photos/x.jpg");
        _captured.Verb.Should().Be(HttpVerb.GET);
        _captured.Expires.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetDownloadUrlAsync_ForeignUri_ThrowsArgumentException()
    {
        var act = () => _storage.GetDownloadUrlAsync(
            new Uri("https://evil.example.com/photos/x.jpg"), TimeSpan.FromMinutes(5));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
