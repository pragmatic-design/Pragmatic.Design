using FluentFTP;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers <see cref="FtpFileStorage.SaveAsync" />: POSIX remote-path shape, the returned
///     <c>ftp://</c> URI, and up-front rejection of oversized seekable uploads.
/// </summary>
public sealed class FtpFileStorageSaveTests
{
    [Fact]
    public async Task SaveAsync_BuildsPosixRemotePathUnderContainer()
    {
        var ctx = FtpStorageTestContext.Create();
        string? capturedPath = null;
        ctx.Client.UploadStream.Returns(args =>
        {
            capturedPath = (string?)args[1];
            return Task.FromResult(FtpStatus.Success);
        });
        using var stream = new MemoryStream([1, 2, 3]);

        await ctx.Storage.SaveAsync(stream, "photo.jpg", "photos");

        capturedPath.Should().NotBeNull();
        capturedPath!.Should().MatchRegex("^/photos/[0-9a-f]{32}\\.jpg$");
    }

    [Fact]
    public async Task SaveAsync_UsesForwardSlashesNotBackslashes()
    {
        var ctx = FtpStorageTestContext.Create(new FtpStorageOptions
        {
            Host = "ftp.example.com",
            Username = "user",
            BasePath = "/data",
        });
        string? capturedPath = null;
        ctx.Client.UploadStream.Returns(args =>
        {
            capturedPath = (string?)args[1];
            return Task.FromResult(FtpStatus.Success);
        });
        using var stream = new MemoryStream([1, 2, 3]);

        await ctx.Storage.SaveAsync(stream, "photo.jpg", "photos");

        capturedPath.Should().StartWith("/data/photos/");
        capturedPath!.Should().Contain("/").And.NotContain("\\");
    }

    [Fact]
    public async Task SaveAsync_CreatesRemoteDirectoryAndOverwrites()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.UploadStream.Returns(Task.FromResult(FtpStatus.Success));
        using var stream = new MemoryStream([1, 2, 3]);

        await ctx.Storage.SaveAsync(stream, "photo.jpg", "photos");

        // Six parameters is past the typed arities, so the check reads the argument list directly.
        ctx.Client.UploadStream.Received(1, args =>
            (FtpRemoteExists)args[2]! == FtpRemoteExists.Overwrite && (bool)args[3]!);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFtpSchemeUriMatchingRemotePath()
    {
        var ctx = FtpStorageTestContext.Create();
        string? capturedPath = null;
        ctx.Client.UploadStream.Returns(args =>
        {
            capturedPath = (string?)args[1];
            return Task.FromResult(FtpStatus.Success);
        });
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await ctx.Storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.Scheme.Should().Be("ftp");
        uri.Host.Should().Be("ftp.example.com");
        uri.AbsolutePath.Should().Be(capturedPath);
    }

    [Fact]
    public async Task SaveAsync_KnownExtension_DetectsContentTypeViaRoundTripInfo()
    {
        // Content type is applied on GetInfo, not upload; this test simply proves the extension is
        // preserved in the stored name so MimeTypes can resolve it later.
        var ctx = FtpStorageTestContext.Create();
        string? capturedPath = null;
        ctx.Client.UploadStream.Returns(args =>
        {
            capturedPath = (string?)args[1];
            return Task.FromResult(FtpStatus.Success);
        });
        using var stream = new MemoryStream([1, 2, 3]);

        await ctx.Storage.SaveAsync(stream, "report.pdf", "docs");

        capturedPath.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task SaveAsync_SeekableStreamOverLimit_ThrowsBeforeConnecting()
    {
        var ctx = FtpStorageTestContext.Create(new FtpStorageOptions
        {
            Host = "ftp.example.com",
            Username = "user",
            MaxFileSizeBytes = 4,
        });
        using var stream = new MemoryStream(new byte[8]);

        var act = () => ctx.Storage.SaveAsync(stream, "big.bin", "uploads");

        await act.Should().ThrowAsync<FileSizeLimitExceededException>();
        ctx.Factory.Create.DidNotReceive();
    }

    [Fact]
    public async Task SaveAsync_UploadFails_ThrowsIOException()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.UploadStream.Returns(Task.FromResult(FtpStatus.Failed));
        using var stream = new MemoryStream([1, 2, 3]);

        var act = () => ctx.Storage.SaveAsync(stream, "photo.jpg", "photos");

        await act.Should().ThrowAsync<IOException>();
    }
}
