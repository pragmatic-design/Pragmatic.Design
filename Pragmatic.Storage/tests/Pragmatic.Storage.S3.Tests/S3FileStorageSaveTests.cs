using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the <see cref="S3FileStorage.SaveAsync" /> happy path: object key shape
///     (prefix/container/GUID/extension), content-type detection and returned URI
///     (s3:// scheme vs configured public base URL).
/// </summary>
public sealed class S3FileStorageSaveTests
{
    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();
    private PutObjectRequest? _captured;

    private S3FileStorage CreateStorage(S3StorageOptions options)
    {
        _s3.PutObjectAsync.When(Arg.Do<PutObjectRequest>(r => _captured = r), Arg.Any<CancellationToken>())
.Returns(new PutObjectResponse());
        return new S3FileStorage(_s3, options, NullLogger<S3FileStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_WithKeyPrefix_BuildsPrefixContainerGuidExtensionKey()
    {
        var storage = CreateStorage(new S3StorageOptions { BucketName = "bkt", KeyPrefix = "uploads/" });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        _captured.Should().NotBeNull();
        _captured!.BucketName.Should().Be("bkt");
        _captured.Key.Should().MatchRegex("^uploads/photos/[0-9a-f]{32}\\.jpg$");
    }

    [Fact]
    public async Task SaveAsync_KnownExtension_SetsContentTypeFromExtension()
    {
        var storage = CreateStorage(new S3StorageOptions { BucketName = "bkt" });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "report.pdf", "docs");

        _captured!.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task SaveAsync_WithoutPublicBaseUrl_ReturnsS3SchemeUri()
    {
        var storage = CreateStorage(new S3StorageOptions { BucketName = "bkt" });
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.Scheme.Should().Be("s3");
        uri.Host.Should().Be("bkt");
        uri.AbsolutePath.TrimStart('/').Should().Be(_captured!.Key);
    }

    [Fact]
    public async Task SaveAsync_WithPublicBaseUrl_ReturnsPublicUri()
    {
        var storage = CreateStorage(new S3StorageOptions
        {
            BucketName = "bkt",
            PublicBaseUrl = "https://cdn.example.com/",
        });
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.ToString().Should().Be($"https://cdn.example.com/{_captured!.Key}");
    }
}
