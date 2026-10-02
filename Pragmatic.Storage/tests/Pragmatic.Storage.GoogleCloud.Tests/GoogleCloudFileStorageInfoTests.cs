using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability of <see cref="GoogleCloudFileStorage" />:
///     <see cref="GoogleCloudFileStorage.GetInfoAsync" /> maps object metadata to a
///     <see cref="StoredFileInfo" />, returns null on a 404, and rejects a foreign URI via name
///     resolution before any SDK call.
/// </summary>
public sealed class GoogleCloudFileStorageInfoTests
{
    private static readonly Uri FileUri = new("gs://bkt/photos/x.png");

    private readonly StorageClientMock _gcs = new StorageClientMock();
    private readonly GoogleCloudFileStorage _storage;

    public GoogleCloudFileStorageInfoTests()
        => _storage = new GoogleCloudFileStorage(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);

    [Fact]
    public async Task GetInfoAsync_ExistingObject_MapsMetadataToStoredFileInfo()
    {
        var updated = new DateTimeOffset(2026, 7, 13, 10, 30, 0, TimeSpan.Zero);
        _gcs.GetObjectAsyncSetup.When("bkt", "photos/x.png", Arg.Any<GetObjectOptions>(), Arg.Any<CancellationToken>())
.Returns(Task.FromResult<StorageObject>(new StorageObject
            {
                Size = 4096,
                ContentType = "image/png",
                UpdatedDateTimeOffset = updated,
            }));

        var info = await _storage.GetInfoAsync(FileUri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4096);
        info.ContentType.Should().Be("image/png");
        info.LastModified.Should().Be(updated);
        info.FileUri.Should().Be(FileUri);
    }

    [Fact]
    public async Task GetInfoAsync_NotFound_ReturnsNull()
    {
        _gcs.GetObjectAsyncSetup.When("bkt", "photos/x.png", Arg.Any<GetObjectOptions>(), Arg.Any<CancellationToken>())
.Returns(Task.FromException<StorageObject>(GcsTestHelpers.NotFound()));

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
