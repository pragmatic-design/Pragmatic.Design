using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the <see cref="GoogleCloudFileStorage.SaveAsync" /> happy path: object-name shape
///     (prefix/container/GUID/extension), content-type detection and returned URI
///     (gs:// scheme vs configured public base URL).
/// </summary>
public sealed class GoogleCloudFileStorageSaveTests
{
    private readonly StorageClientMock _gcs = new StorageClientMock();
    private string? _capturedObjectName;
    private string? _capturedContentType;

    private GoogleCloudFileStorage CreateStorage(GoogleCloudStorageOptions options)
    {
        // Seven parameters: the object name and content type are read off the argument list.
        _gcs.UploadObjectAsync7Setup.Returns(args =>
        {
            _capturedObjectName = (string?)args[1];
            _capturedContentType = (string?)args[2];
            return Task.FromResult(new StorageObject());
        });

        return new GoogleCloudFileStorage(_gcs, options, NullLogger<GoogleCloudFileStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_WithObjectPrefix_BuildsPrefixContainerGuidExtensionName()
    {
        var storage = CreateStorage(new GoogleCloudStorageOptions { BucketName = "bkt", ObjectPrefix = "uploads/" });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        _capturedObjectName.Should().MatchRegex("^uploads/photos/[0-9a-f]{32}\\.jpg$");
    }

    [Fact]
    public async Task SaveAsync_KnownExtension_SetsContentTypeFromExtension()
    {
        var storage = CreateStorage(new GoogleCloudStorageOptions { BucketName = "bkt" });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "report.pdf", "docs");

        _capturedContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task SaveAsync_WithoutPublicBaseUrl_ReturnsGsSchemeUri()
    {
        var storage = CreateStorage(new GoogleCloudStorageOptions { BucketName = "bkt" });
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.Scheme.Should().Be("gs");
        uri.Host.Should().Be("bkt");
        uri.AbsolutePath.TrimStart('/').Should().Be(_capturedObjectName);
    }

    [Fact]
    public async Task SaveAsync_WithPublicBaseUrl_ReturnsPublicUri()
    {
        var storage = CreateStorage(new GoogleCloudStorageOptions
        {
            BucketName = "bkt",
            PublicBaseUrl = "https://cdn.example.com/",
        });
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.ToString().Should().Be($"https://cdn.example.com/{_capturedObjectName}");
    }
}
