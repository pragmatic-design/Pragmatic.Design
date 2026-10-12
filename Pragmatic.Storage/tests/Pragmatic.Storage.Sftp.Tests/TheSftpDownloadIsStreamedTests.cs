using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     <see cref="SftpFileStorage.GetAsync" /> hands the file to the caller as it arrives, keeping its
///     connection open until the transfer ends, instead of downloading the whole file into memory
///     before closing the connection.
/// </summary>
/// <remarks>
///     The stand-in client writes a 100 MB file in 1 MB chunks and counts what it has handed over.
///     The reader counts what it has read. The difference, at every read, is what the provider holds
///     on behalf of the caller.
/// </remarks>
public sealed class TheSftpDownloadIsStreamedTests : SftpStorageTestBase
{
    private const int ChunkSize = 1024 * 1024;
    private const int ChunkCount = 100;
    private const string RemotePath = "/videos/large.mp4";

    private static readonly Uri FileUri = new("sftp://host/videos/large.mp4");

    private long _written;

    public TheSftpDownloadIsStreamedTests()
        => Client.ExistsAsync.When(RemotePath, Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task GetAsync_LargeFile_HoldsAtMostOneWriteAheadOfTheReader()
    {
        Client.DownloadFileAsync3.Returns((_, target, ct) => WriteChunksAsync(target, ct));

        using var stream = await CreateStorage().GetAsync(FileUri);

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
    public async Task GetAsync_StreamDisposedBeforeTheEnd_CancelsTheTransferAndClosesTheConnection()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Client.DownloadFileAsync3.Returns((_, target, ct) =>
        {
            ct.Register(() => cancelled.TrySetResult());
            return WriteChunksAsync(target, ct);
        });

        var stream = await CreateStorage().GetAsync(FileUri);
        await stream!.ReadExactlyAsync(new byte[16]);
        Client.Dispose.Received(0);
        await stream.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Client.Dispose.Received(1);
    }

    private async Task WriteChunksAsync(Stream destination, CancellationToken ct)
    {
        var chunk = new byte[ChunkSize];
        for (var i = 0; i < ChunkCount; i++)
        {
            // Counted when handed over, not when the write returns: a write the provider is still
            // holding is memory the provider holds.
            Interlocked.Add(ref _written, ChunkSize);
            await destination.WriteAsync(chunk, ct).ConfigureAwait(false);
        }
    }
}
