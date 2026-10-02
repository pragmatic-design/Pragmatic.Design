using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the not-found semantics of <see cref="S3FileStorage" />: Get returns null,
///     Exists returns false, and Delete is an idempotent no-op (404 or NoSuchKey).
/// </summary>
public sealed class S3FileStorageNotFoundTests
{
    private static readonly Uri FileUri = new("s3://bkt/photos/missing.jpg");

    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();
    private readonly S3FileStorage _storage;

    public S3FileStorageNotFoundTests()
        => _storage = new S3FileStorage(_s3, new S3StorageOptions { BucketName = "bkt" },
            NullLogger<S3FileStorage>.Instance);

    private static AmazonS3Exception NotFound()
        => new("not found") { StatusCode = HttpStatusCode.NotFound };

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        _s3.GetObjectAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(Task.FromException<GetObjectResponse>(NotFound()));

        var stream = await _storage.GetAsync(FileUri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_NotFound_ReturnsFalse()
    {
        _s3.GetObjectMetadataAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(Task.FromException<GetObjectMetadataResponse>(NotFound()));

        var exists = await _storage.ExistsAsync(FileUri);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_Found_ReturnsTrue()
    {
        _s3.GetObjectMetadataAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(new GetObjectMetadataResponse());

        var exists = await _storage.ExistsAsync(FileUri);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_IsNoOp()
    {
        _s3.DeleteObjectAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(Task.FromException<DeleteObjectResponse>(NotFound()));

        var act = () => _storage.DeleteAsync(FileUri);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_NoSuchKeyErrorCode_IsNoOp()
    {
        _s3.DeleteObjectAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(Task.FromException<DeleteObjectResponse>(
                new AmazonS3Exception("gone") { ErrorCode = "NoSuchKey" }));

        var act = () => _storage.DeleteAsync(FileUri);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_Existing_CallsDeleteObject()
    {
        _s3.DeleteObjectAsync3.When("bkt", "photos/missing.jpg", Arg.Any<CancellationToken>())
.Returns(new DeleteObjectResponse());

        await _storage.DeleteAsync(FileUri);

        _s3.DeleteObjectAsync3.Received(1, "bkt", "photos/missing.jpg", Arg.Any<CancellationToken>());
    }
}
