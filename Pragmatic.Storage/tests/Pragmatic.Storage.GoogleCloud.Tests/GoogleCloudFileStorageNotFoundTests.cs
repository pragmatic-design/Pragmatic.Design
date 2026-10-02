using Google.Apis.Download;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the not-found semantics of <see cref="GoogleCloudFileStorage" />: Get returns null,
///     Exists returns false, and Delete is an idempotent no-op on a 404.
/// </summary>
public sealed class GoogleCloudFileStorageNotFoundTests
{
    private static readonly Uri FileUri = new("gs://bkt/photos/missing.jpg");

    private readonly StorageClientMock _gcs = new StorageClientMock();
    private readonly GoogleCloudFileStorage _storage;

    public GoogleCloudFileStorageNotFoundTests()
        => _storage = new GoogleCloudFileStorage(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        _gcs.DownloadObjectAsync6Setup.Returns(Task.FromException<StorageObject>(GcsTestHelpers.NotFound()));

        var stream = await _storage.GetAsync(FileUri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_NotFound_ReturnsFalse()
    {
        _gcs.GetObjectAsyncSetup.When("bkt", "photos/missing.jpg", Arg.Any<GetObjectOptions>(), Arg.Any<CancellationToken>())
.Returns(Task.FromException<StorageObject>(GcsTestHelpers.NotFound()));

        var exists = await _storage.ExistsAsync(FileUri);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_Found_ReturnsTrue()
    {
        _gcs.GetObjectAsyncSetup.When("bkt", "photos/missing.jpg", Arg.Any<GetObjectOptions>(), Arg.Any<CancellationToken>())
.Returns(Task.FromResult<StorageObject>(new StorageObject()));

        var exists = await _storage.ExistsAsync(FileUri);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_IsNoOp()
    {
        _gcs.DeleteObjectAsync4Setup.When("bkt", "photos/missing.jpg", Arg.Any<DeleteObjectOptions>(), Arg.Any<CancellationToken>())
.Returns(Task.FromException(GcsTestHelpers.NotFound()));

        var act = () => _storage.DeleteAsync(FileUri);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_Existing_CallsDeleteObject()
    {
        await _storage.DeleteAsync(FileUri);

        _gcs.DeleteObjectAsync4Setup.Received(1, "bkt", "photos/missing.jpg", Arg.Any<DeleteObjectOptions>(), Arg.Any<CancellationToken>());
    }
}
