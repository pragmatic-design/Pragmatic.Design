using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability of <see cref="S3FileStorage" />:
///     <see cref="S3FileStorage.GetInfoAsync" /> maps object metadata to a
///     <see cref="StoredFileInfo" />, returns null on a 404, and rejects a foreign URI via key
///     resolution before any SDK call.
/// </summary>
public sealed class S3FileStorageInfoTests
{
    private static readonly Uri FileUri = new("s3://bkt/photos/x.png");

    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();
    private readonly S3FileStorage _storage;

    public S3FileStorageInfoTests()
        => _storage = new S3FileStorage(_s3, new S3StorageOptions { BucketName = "bkt" },
            NullLogger<S3FileStorage>.Instance);

    [Fact]
    public async Task GetInfoAsync_ExistingObject_MapsMetadataToStoredFileInfo()
    {
        var lastModified = new DateTime(2026, 7, 13, 10, 30, 0, DateTimeKind.Utc);
        var response = new GetObjectMetadataResponse { ContentLength = 4096, LastModified = lastModified };
        response.Headers.ContentType = "image/png";
        _s3.GetObjectMetadataAsync3.When("bkt", "photos/x.png", Arg.Any<CancellationToken>())
.Returns(response);

        var info = await _storage.GetInfoAsync(FileUri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4096);
        info.ContentType.Should().Be("image/png");
        info.LastModified.Should().Be(new DateTimeOffset(lastModified));
        info.FileUri.Should().Be(FileUri);
    }

    [Fact]
    public async Task GetInfoAsync_NotFound_ReturnsNull()
    {
        _s3.GetObjectMetadataAsync3.When("bkt", "photos/x.png", Arg.Any<CancellationToken>())
.Returns(Task.FromException<GetObjectMetadataResponse>(
                new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound }));

        var info = await _storage.GetInfoAsync(FileUri);

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_ForeignUri_ThrowsArgumentException()
    {
        var act = () => _storage.GetInfoAsync(new Uri("https://evil.example.com/photos/x.png"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
