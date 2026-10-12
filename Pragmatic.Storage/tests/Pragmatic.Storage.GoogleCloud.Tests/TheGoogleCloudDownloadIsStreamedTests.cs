using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     <see cref="GoogleCloudFileStorage.GetAsync" /> hands the object to the caller as it arrives,
///     instead of downloading it whole into memory first.
/// </summary>
/// <remarks>
///     The stand-in client writes a 100 MB object in 1 MB chunks and counts what it has written. The
///     reader counts what it has read. The difference, at every read, is what the provider holds on
///     behalf of the caller: a buffered download holds the whole object before the first byte is
///     read (104,857,600 bytes, measured); a streamed one holds the one write still in flight
///     (1,048,576 bytes here: the pipe takes a write whole, then pauses the writer).
/// </remarks>
public sealed class TheGoogleCloudDownloadIsStreamedTests
{
    private const int ChunkSize = 1024 * 1024;
    private const int ChunkCount = 100;

    private static readonly Uri FileUri = new("gs://bkt/videos/large.mp4");

    private readonly StorageClientMock _gcs = new();
    private readonly GoogleCloudFileStorage _storage;
    private long _written;

    public TheGoogleCloudDownloadIsStreamedTests()
        => _storage = new GoogleCloudFileStorage(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);

    [Fact]
    public async Task GetAsync_LargeObject_HoldsAtMostAFewChunksAheadOfTheReader()
    {
        _gcs.DownloadObjectAsync6Setup.Returns(args => WriteChunksAsync((Stream)args[2]!, (CancellationToken)args[4]!));

        using var stream = await _storage.GetAsync(FileUri);

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
            "the provider should hand the object over as it arrives, not hold all of it before the first read");
    }

    [Fact]
    public async Task GetAsync_DownloadFailsMidway_TheReaderSeesTheFailure()
    {
        // The failure is held back until the caller has the stream: it is the midway case, not a
        // download that fails before GetAsync returns (which GetAsync itself throws).
        var handedOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gcs.DownloadObjectAsync6Setup.Returns(args => WriteThenFailAsync((Stream)args[2]!, handedOver.Task));

        using var stream = await _storage.GetAsync(FileUri);
        handedOver.SetResult();

        stream.Should().NotBeNull();
        var act = () => stream!.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<IOException>().WithMessage("connection reset");
    }

    [Fact]
    public async Task GetAsync_StreamDisposedBeforeTheEnd_CancelsTheDownload()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gcs.DownloadObjectAsync6Setup.Returns(args =>
        {
            ((CancellationToken)args[4]!).Register(() => cancelled.TrySetResult());
            return WriteChunksAsync((Stream)args[2]!, (CancellationToken)args[4]!);
        });

        var stream = await _storage.GetAsync(FileUri);
        await stream!.ReadExactlyAsync(new byte[16]);
        await stream.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<StorageObject> WriteChunksAsync(Stream destination, CancellationToken ct)
    {
        var chunk = new byte[ChunkSize];
        for (var i = 0; i < ChunkCount; i++)
        {
            // Counted when handed over, not when the write returns: a write the provider is still
            // holding is memory the provider holds.
            Interlocked.Add(ref _written, ChunkSize);
            await destination.WriteAsync(chunk, ct).ConfigureAwait(false);
        }

        return new StorageObject();
    }

    private static async Task<StorageObject> WriteThenFailAsync(Stream destination, Task handedOver)
    {
        await destination.WriteAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
        // Bounded: a provider that waits for the whole download before returning never hands the
        // stream over, and the test should fail rather than hang.
        await handedOver.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        throw new IOException("connection reset");
    }
}
