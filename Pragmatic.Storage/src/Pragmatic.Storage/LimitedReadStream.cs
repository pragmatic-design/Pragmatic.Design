namespace Pragmatic.Storage;

/// <summary>
///     A read-only stream wrapper that enforces a maximum number of bytes that may be
///     read from an inner stream. Once the running total of bytes read exceeds
///     <paramref name="maxBytes" />, the next read throws <see cref="FileSizeLimitExceededException" />
///     (an <see cref="InvalidOperationException" />).
/// </summary>
/// <remarks>
///     Use this to enforce an upload size cap on a <strong>non-seekable</strong> stream (where the
///     length is not known up front), so the limit is honored while the bytes are being consumed
///     by the destination (e.g. handed to an S3 <c>PutObject</c> request) rather than only on the
///     seekable fast-path. The error message mirrors the one used by the storage providers for an
///     over-limit upload.
/// </remarks>
public sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _total;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Count(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    private void Count(int read)
    {
        if (read <= 0)
            return;

        _total += read;
        if (_total > maxBytes)
            throw new FileSizeLimitExceededException(
                $"Upload rejected: stream exceeds the limit of {maxBytes} bytes.",
                maxBytes);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
