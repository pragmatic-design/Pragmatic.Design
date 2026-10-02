using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability of <see cref="SftpFileStorage" />:
///     <see cref="SftpFileStorage.GetInfoAsync" /> maps SFTP attributes to a
///     <see cref="StoredFileInfo" /> (size, last-modified, content type from extension), and returns
///     null when the file is absent.
/// </summary>
public sealed class SftpFileStorageInfoTests : SftpStorageTestBase
{
    private static readonly Uri FileUri = new("sftp://host/docs/report.pdf");
    private const string RemotePath = "/docs/report.pdf";

    [Fact]
    public async Task GetInfoAsync_ExistingFile_MapsAttributesToStoredFileInfo()
    {
        var lastWrite = new DateTime(2026, 7, 13, 10, 30, 0, DateTimeKind.Utc);
        var file = new SftpFileMock();
        file.Length.Returns(4096L);
        file.LastWriteTimeUtc.Returns(lastWrite);
        Client.GetAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(file);
        var storage = CreateStorage();

        var info = await storage.GetInfoAsync(FileUri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4096);
        info.ContentType.Should().Be("application/pdf");
        info.LastModified.Should().Be(new DateTimeOffset(lastWrite));
        info.FileUri.Should().Be(FileUri);
    }

    [Fact]
    public async Task GetInfoAsync_MissingFile_ReturnsNull()
    {
        Client.GetAsync.When(RemotePath, Arg.Any<CancellationToken>())
.Throws(new SftpPathNotFoundException("not found"));
        var storage = CreateStorage();

        var info = await storage.GetInfoAsync(FileUri);

        info.Should().BeNull();
    }
}
