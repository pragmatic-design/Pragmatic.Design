using Azure;
using Azure.Storage.Blobs.Models;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability of <see cref="AzureBlobFileStorage" />:
///     <see cref="AzureBlobFileStorage.GetInfoAsync" /> maps blob properties to a
///     <see cref="StoredFileInfo" /> and returns null on a 404. The substitute chain is exercised via
///     the account-owned container/blob clients (so private blobs are supported).
/// </summary>
public sealed class AzureBlobFileStorageInfoTests
{
    private readonly AzureStorageSubstituteFixture _fixture = new();
    private readonly AzureBlobFileStorage _storage;

    public AzureBlobFileStorageInfoTests()
        => _storage = _fixture.CreateStorage();

    [Fact]
    public async Task GetInfoAsync_ExistingBlob_MapsPropertiesToStoredFileInfo()
    {
        var lastModified = new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);
        var properties = BlobsModelFactory.BlobProperties(
            lastModified: lastModified,
            contentLength: 2048,
            contentType: "application/pdf");
        _fixture.Blob.GetPropertiesAsyncSetup.Returns(Task.FromResult(Response.FromValue(properties, null!)));

        var info = await _storage.GetInfoAsync(_fixture.BlobUri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(2048);
        info.ContentType.Should().Be("application/pdf");
        info.LastModified.Should().Be(lastModified);
        info.FileUri.Should().Be(_fixture.BlobUri);
    }

    [Fact]
    public async Task GetInfoAsync_NotFound_ReturnsNull()
    {
        _fixture.Blob.GetPropertiesAsyncSetup.Throws(new RequestFailedException(404, "blob not found"));

        var info = await _storage.GetInfoAsync(_fixture.BlobUri);

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_ForeignUri_ThrowsArgumentException()
    {
        var act = () => _storage.GetInfoAsync(
            new Uri("https://evil.blob.core.windows.net/photos/blob.bin"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }
}
