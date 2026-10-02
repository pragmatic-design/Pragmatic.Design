using Azure.Storage.Blobs.Models;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the <see cref="AzureBlobFileStorage.SaveAsync" /> happy path: container naming
///     (with and without prefix), GUID blob-name shape, content-type detection and returned URI.
/// </summary>
public sealed class AzureBlobFileStorageSaveTests
{
    private readonly AzureStorageSubstituteFixture _fixture = new();

    [Fact]
    public async Task SaveAsync_WithDefaults_UsesContainerNameAndGuidBlobName()
    {
        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        _fixture.RequestedContainerName.Should().Be("photos");
        _fixture.RequestedBlobName.Should().MatchRegex("^[0-9a-f]{32}\\.jpg$");
        uri.Should().Be(_fixture.BlobUri);
    }

    [Fact]
    public async Task SaveAsync_WithContainerPrefix_PrependsPrefix()
    {
        var storage = _fixture.CreateStorage(new AzureBlobStorageOptions { ContainerPrefix = "myapp-" });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        _fixture.RequestedContainerName.Should().Be("myapp-photos");
    }

    [Fact]
    public async Task SaveAsync_KnownExtension_SetsContentTypeFromExtension()
    {
        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        _fixture.UploadHeaders.Should().NotBeNull();
        _fixture.UploadHeaders!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SaveAsync_UnknownExtension_FallsBackToOctetStream()
    {
        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "data.unknownext", "imports");

        _fixture.UploadHeaders!.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task SaveAsync_SameContainerTwice_CallsCreateIfNotExistsOnce()
    {
        var storage = _fixture.CreateStorage();
        using var first = new MemoryStream([1]);
        using var second = new MemoryStream([2]);

        await storage.SaveAsync(first, "a.bin", "docs");
        await storage.SaveAsync(second, "b.bin", "docs");

        // The 3-parameter convenience overload is non-virtual sugar: the interceptable call
        // is the 4-parameter virtual overload with BlobContainerEncryptionScopeOptions.
        _fixture.Container.CreateIfNotExistsAsync4Setup.Received(1);
    }
}
