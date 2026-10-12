using System.IO.Pipelines;

namespace Pragmatic.Storage;

/// <summary>
///     A read-only stream over a download that is still running, for providers whose client writes
///     into a destination stream instead of returning the response body.
/// </summary>
/// <remarks>
///     <para>
///         Downloading into a <see cref="MemoryStream" /> holds the whole file in memory before the
///         first byte reaches the caller. Here the download writes into a <see cref="Pipe" /> and the
///         caller reads the other end: the pipe takes each write whole and pauses the writer once
///         64 KB are unread, so what is held is bounded by that and one write of the client, not by
///         the file's size.
///     </para>
///     <para>
///         The download delegate owns everything it needs for the transfer, a connection included,
///         and keeps it until the transfer ends: a provider with a connection per operation opens
///         and closes it inside the delegate. A failed download completes the pipe with its
///         exception, which the caller's next read rethrows. Disposing the stream before the end
///         cancels the download.
///     </para>
/// </remarks>
public sealed class PipedDownloadStream : Stream
{
    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _cancellation;
    private readonly Stream _reader;
    private Task _pump = Task.CompletedTask;
    private bool _found = true;
    private bool _disposed;

    private PipedDownloadStream(CancellationToken ct)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reader = _pipe.Reader.AsStream();
    }

    /// <summary>
    ///     Starts <paramref name="download" /> writing into a new stream, and returns that stream once
    ///     the download has produced its first bytes or ended.
    /// </summary>
    /// <param name="download">
    ///     Writes the file into the destination it is given and returns <see langword="true" />, or
    ///     returns <see langword="false" /> without writing anything when the file does not exist.
    ///     The token it is given is cancelled when the caller disposes the stream.
    /// </param>
    /// <param name="ct">Cancels the download, both before and after the stream is returned.</param>
    /// <returns>
    ///     The stream, or <see langword="null" /> when <paramref name="download" /> returned
    ///     <see langword="false" />. A download that fails before writing anything throws here.
    /// </returns>
    public static async Task<Stream?> StartAsync(Func<Stream, CancellationToken, Task<bool>> download, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(download);

        var stream = new PipedDownloadStream(ct);
        // Run on the pool: a download that writes synchronously before its first await would
        // otherwise fill the pipe on the caller's thread, before anyone is there to read it.
        stream._pump = Task.Run(() => stream.PumpAsync(download), CancellationToken.None);

        var handedOver = false;
        try
        {
            var first = await stream._pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
            var nothingThere = first.IsCompleted && first.Buffer.IsEmpty && !stream._found;
            stream._pipe.Reader.AdvanceTo(first.Buffer.Start);
            if (nothingThere)
                return null;

            handedOver = true;
            return stream;
        }
        finally
        {
            if (!handedOver)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(Func<Stream, CancellationToken, Task<bool>> download)
    {
        var destination = _pipe.Writer.AsStream(leaveOpen: true);
        try
        {
            _found = await download(destination, _cancellation.Token).ConfigureAwait(false);
            await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Handed to the reader, which rethrows it: a failed download is the caller's failure.
            await _pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _reader.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _reader.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _reader.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _cancellation.CancelAsync().ConfigureAwait(false);
        await _reader.DisposeAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _cancellation.Cancel();
            _reader.Dispose();
            // The pump never faults (it hands its exception to the pipe), and waiting for it here
            // would block on async work: the token source is released once it has stopped.
            _pump.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), _cancellation,
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        base.Dispose(disposing);
    }
}
