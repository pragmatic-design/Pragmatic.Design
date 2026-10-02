using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers <see cref="SftpFileStorage.ExistsAsync" /> (true/false) and
///     <see cref="SftpFileStorage.DeleteAsync" /> (deletes an existing file; missing file is an
///     idempotent no-op).
/// </summary>
public sealed class SftpFileStorageExistsDeleteTests : SftpStorageTestBase
{
    private static readonly Uri FileUri = new("sftp://host/photos/file.bin");
    private const string RemotePath = "/photos/file.bin";

    [Fact]
    public async Task ExistsAsync_Found_ReturnsTrue()
    {
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(true);
        var storage = CreateStorage();

        (await storage.ExistsAsync(FileUri)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NotFound_ReturnsFalse()
    {
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(false);
        var storage = CreateStorage();

        (await storage.ExistsAsync(FileUri)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_CallsDeleteFile()
    {
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(true);
        var storage = CreateStorage();

        await storage.DeleteAsync(FileUri);

        Client.DeleteFileAsync.Received(1, RemotePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_IsNoOp()
    {
        Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(false);
        var storage = CreateStorage();

        var act = () => storage.DeleteAsync(FileUri);

        await act.Should().NotThrowAsync();
        Client.DeleteFileAsync.DidNotReceive();
    }
}
