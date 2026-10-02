using FluentFTP;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     Covers <see cref="FtpFileStorage.GetAsync" /> and <see cref="FtpFileStorage.ExistsAsync" />:
///     download round-trip, missing-file null semantics, and existence checks.
/// </summary>
public sealed class FtpFileStorageReadTests
{
    private static readonly Uri FileUri = new("ftp://ftp.example.com/photos/abc.jpg");
    private const string RemotePath = "/photos/abc.jpg";

    [Fact]
    public async Task GetAsync_ExistingFile_ReturnsBufferedRoundTrip()
    {
        var ctx = FtpStorageTestContext.Create();
        var payload = new byte[] { 10, 20, 30, 40 };
        ctx.Client.DownloadStream.Returns(args =>
        {
            var target = (Stream)args[0]!;
            target.Write(payload, 0, payload.Length);
            return Task.FromResult(true);
        });

        var result = await ctx.Storage.GetAsync(FileUri);

        result.Should().NotBeNull();
        using var buffer = new MemoryStream();
        await result!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(payload);
        await result.DisposeAsync();
    }

    [Fact]
    public async Task GetAsync_MissingFile_ReturnsNull()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.DownloadStream.Returns(Task.FromResult(false));

        var result = await ctx.Storage.GetAsync(FileUri);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ForeignUriScheme_ThrowsArgumentException()
    {
        var ctx = FtpStorageTestContext.Create();

        var act = () => ctx.Storage.GetAsync(new Uri("https://evil.example.com/photos/abc.jpg"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("fileUri");
    }

    [Fact]
    public async Task ExistsAsync_PresentFile_ReturnsTrue()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.FileExists.When(RemotePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        (await ctx.Storage.ExistsAsync(FileUri)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingFile_ReturnsFalse()
    {
        var ctx = FtpStorageTestContext.Create();
        ctx.Client.FileExists.When(RemotePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        (await ctx.Storage.ExistsAsync(FileUri)).Should().BeFalse();
    }
}
