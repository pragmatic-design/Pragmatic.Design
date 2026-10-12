using FluentFTP;
using FluentFTP.Exceptions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Ftp.Tests;

/// <summary>
///     <see cref="FtpFileStorage.GetAsync" /> hands the file to the caller as it arrives, keeping its
///     connection open until the transfer ends, instead of downloading the whole file into memory
///     before closing the connection.
/// </summary>
/// <remarks>
///     The stand-in client writes a 100 MB file in 1 MB chunks and counts what it has handed over.
///     The reader counts what it has read. The difference, at every read, is what the provider holds
///     on behalf of the caller.
/// </remarks>
public sealed class TheFtpDownloadIsStreamedTests
{
    private const int ChunkSize = 1024 * 1024;
    private const int ChunkCount = 100;
    private const string RemotePath = "/videos/large.mp4";

    private static readonly Uri FileUri = new("ftp://ftp.example.com/videos/large.mp4");

    private readonly FtpStorageTestContext _ctx = FtpStorageTestContext.Create();
    private long _written;

    public TheFtpDownloadIsStreamedTests()
        => _ctx.Client.FileExists.When(RemotePath, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

    [Fact]
    public async Task GetAsync_LargeFile_HoldsAtMostOneWriteAheadOfTheReader()
    {
        _ctx.Client.DownloadStream.Returns(args => WriteChunksAsync((Stream)args[0]!, (CancellationToken)args[4]!));

        using var stream = await _ctx.Storage.GetAsync(FileUri);

        stream.Should().NotBeNull();
        long read = 0;
        long mostHeldAhead = 0;
        var buffer = new byte[64 * 1024];
        int n;
        while ((n = await stream!.ReadAsync(buffer)) > 0)
        {
            mostHeldAhead = Math.Max(mostHeldAhead, Interlocked.Read(ref _written) - read);
            read += n;
        }

        read.Should().Be((long)ChunkSize * ChunkCount);
        mostHeldAhead.Should().BeLessThan(2L * ChunkSize,
            "the provider should hand the file over as it arrives, not hold all of it before the first read");
    }

    [Fact]
    public async Task GetAsync_TransferFailsMidway_TheReaderSeesTheFailure()
    {
        // Held back until the caller has the stream, so this is the midway case. Bounded, so a
        // provider that waits for the whole transfer fails the test instead of hanging it.
        var handedOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ctx.Client.DownloadStream.Returns(args => WriteThenFailAsync((Stream)args[0]!, handedOver.Task));

        using var stream = await _ctx.Storage.GetAsync(FileUri);
        handedOver.SetResult();

        stream.Should().NotBeNull();
        var act = () => stream!.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<FtpException>().WithMessage("data connection closed");
    }

    [Fact]
    public async Task GetAsync_StreamDisposedBeforeTheEnd_CancelsTheTransferAndClosesTheConnection()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ctx.Client.DownloadStream.Returns(args =>
        {
            var ct = (CancellationToken)args[4]!;
            ct.Register(() => cancelled.TrySetResult());
            return WriteChunksAsync((Stream)args[0]!, ct);
        });

        var stream = await _ctx.Storage.GetAsync(FileUri);
        await stream!.ReadExactlyAsync(new byte[16]);
        _ctx.Client.DisposeAsync.Received(0);
        await stream.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _ctx.Client.DisposeAsync.Received(1);
    }

    private async Task<bool> WriteChunksAsync(Stream destination, CancellationToken ct)
    {
        var chunk = new byte[ChunkSize];
        for (var i = 0; i < ChunkCount; i++)
        {
            // Counted when handed over, not when the write returns: a write the provider is still
            // holding is memory the provider holds.
            Interlocked.Add(ref _written, ChunkSize);
            await destination.WriteAsync(chunk, ct).ConfigureAwait(false);
        }

        return true;
    }

    private static async Task<bool> WriteThenFailAsync(Stream destination, Task handedOver)
    {
        await destination.WriteAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
        await handedOver.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        throw new FtpException("data connection closed");
    }
}
