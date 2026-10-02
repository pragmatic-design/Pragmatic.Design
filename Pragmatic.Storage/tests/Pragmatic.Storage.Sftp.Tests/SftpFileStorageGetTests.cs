using System.IO;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers <see cref="SftpFileStorage.GetAsync" />: returns null for a missing file, and buffers
///     an existing remote file into a readable stream (round-trip of the bytes).
/// </summary>
public sealed class SftpFileStorageGetTests : SftpStorageTestBase
{
    private static readonly Uri FileUri = new("sftp://host/photos/file.bin");
    private const string RemotePath = "/photos/file.bin";

    [Fact]
    public async Task GetAsync_MissingFile_ReturnsNull()
    {
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(false);
        var storage = CreateStorage();

        var stream = await storage.GetAsync(FileUri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ExistingFile_RoundTripsBytes()
    {
        byte[] payload = [10, 20, 30, 40, 50];
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(true);
        Client.DownloadFileAsync3.Returns((_, target, _) =>
        {
            target.Write(payload, 0, payload.Length);
            return Task.CompletedTask;
        });
        var storage = CreateStorage();

        using var stream = await storage.GetAsync(FileUri);

        stream.Should().NotBeNull();
        using var read = new MemoryStream();
        stream!.CopyTo(read);
        read.ToArray().Should().Equal(payload);
    }
}
