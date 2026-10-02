using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers <see cref="FtpFileStorage.DeleteAsync" />: deletes an existing file and is a no-op
///     (idempotent) for a missing one.
/// </summary>
public sealed class FtpFileStorageDeleteTests
{
    private static readonly Uri FileUri = new("ftp://ftp.example.com/photos/abc.jpg");
    private const string RemotePath = "/photos/abc.jpg";

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesIt()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.FileExists.When(RemotePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        await ctx.Storage.DeleteAsync(FileUri);

        ctx.Client.DeleteFile.Received(1, RemotePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_IsNoOp()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.FileExists.When(RemotePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        await ctx.Storage.DeleteAsync(FileUri);

        ctx.Client.DeleteFile.DidNotReceive();
    }
}
