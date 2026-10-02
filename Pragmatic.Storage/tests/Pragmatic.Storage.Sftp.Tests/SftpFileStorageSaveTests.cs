using System.IO;
using System.Text.RegularExpressions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers the <see cref="SftpFileStorage.SaveAsync" /> happy path: POSIX ('/') remote path shape
///     ({base}/{container}/{GUID}{ext}), remote directory creation, and the returned re-resolvable
///     <c>sftp://</c> URI.
/// </summary>
public sealed class SftpFileStorageSaveTests : SftpStorageTestBase
{
    private string? _uploadedPath;

    public SftpFileStorageSaveTests()
        => Client.UploadFileAsync3.When(
                Arg.Any<Stream>(),
                Arg.Do<string>(p => _uploadedPath = p),
                Arg.Any<CancellationToken>())
.Returns(Task.CompletedTask);

    [Fact]
    public async Task SaveAsync_DefaultBasePath_UploadsToPosixPathWithGuidAndExtension()
    {
        var storage = CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        _uploadedPath.Should().NotBeNull();
        _uploadedPath!.Should().MatchRegex("^/photos/[0-9a-f]{32}\\.jpg$");
        _uploadedPath.Should().NotContain("\\", "remote paths must use POSIX separators");
    }

    [Fact]
    public async Task SaveAsync_CreatesRemoteContainerDirectory()
    {
        var storage = CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        Client.CreateDirectoryAsync.Received(1, "/photos", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_WithBasePathAndNestedContainer_CreatesEachDirectorySegment()
    {
        var storage = CreateStorage(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
            Password = "pass",
            BasePath = "/uploads",
        });
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "doc.pdf", "a/b");

        Client.CreateDirectoryAsync.Received(1, "/uploads", Arg.Any<CancellationToken>());
        Client.CreateDirectoryAsync.Received(1, "/uploads/a", Arg.Any<CancellationToken>());
        Client.CreateDirectoryAsync.Received(1, "/uploads/a/b", Arg.Any<CancellationToken>());
        _uploadedPath.Should().MatchRegex("^/uploads/a/b/[0-9a-f]{32}\\.pdf$");
    }

    [Fact]
    public async Task SaveAsync_ExistingDirectory_IsNotRecreated()
    {
        Client.ExistsAsync.When("/photos", Arg.Any<CancellationToken>()).Returns(true);
        var storage = CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync(stream, "photo.jpg", "photos");

        Client.CreateDirectoryAsync.DidNotReceive("/photos", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsReResolvableSftpUri()
    {
        var storage = CreateStorage();
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.Scheme.Should().Be("sftp");
        uri.Host.Should().Be("host");
        var storedName = Regex.Match(_uploadedPath!, "[0-9a-f]{32}\\.jpg").Value;
        uri.AbsolutePath.Should().Be($"/photos/{storedName}");
    }
}
