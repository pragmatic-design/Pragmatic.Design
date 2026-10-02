using FluentFTP;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability: <see cref="FtpFileStorage.GetInfoAsync" />
///     maps a listing to <see cref="StoredFileInfo" />, derives the content type from the extension,
///     and returns null for a missing object or a non-file entry.
/// </summary>
public sealed class FtpFileStorageInfoTests
{
    private static readonly Uri FileUri = new("ftp://ftp.example.com/photos/x.png");
    private const string RemotePath = "/photos/x.png";

    [Fact]
    public async Task GetInfoAsync_ExistingFile_MapsSizeModifiedAndContentType()
    {
        var ctx = FtpStorageTestContext.Create();
        var modified = new DateTime(2026, 7, 13, 10, 30, 0, DateTimeKind.Utc);
        ctx.Client.GetObjectInfo.When(RemotePath, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FtpListItem { Type = FtpObjectType.File, Size = 4096, Modified = modified }));

        var info = await ctx.Storage.GetInfoAsync(FileUri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4096);
        info.ContentType.Should().Be("image/png");
        info.LastModified.Should().Be(new DateTimeOffset(modified));
        info.FileUri.Should().Be(FileUri);
    }

    [Fact]
    public async Task GetInfoAsync_UnknownModified_LeavesLastModifiedNull()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.GetObjectInfo.When(RemotePath, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FtpListItem { Type = FtpObjectType.File, Size = 10, Modified = DateTime.MinValue }));

        var info = await ctx.Storage.GetInfoAsync(FileUri);

        info.Should().NotBeNull();
        info!.LastModified.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_MissingObject_ReturnsNull()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.GetObjectInfo.When(RemotePath, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FtpListItem>(null!));

        var info = await ctx.Storage.GetInfoAsync(FileUri);

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_Directory_ReturnsNull()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.GetObjectInfo.When(RemotePath, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FtpListItem { Type = FtpObjectType.Directory, Size = 0 }));

        var info = await ctx.Storage.GetInfoAsync(FileUri);

        info.Should().BeNull();
    }
}
