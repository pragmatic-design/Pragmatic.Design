namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     A read-only stream that owns the <see cref="HttpResponseMessage" /> it was read from:
///     disposing the stream disposes the response, and not a moment earlier.
/// </summary>
/// <remarks>
///     <para>
///         The stream returned by <c>HttpContent.ReadAsStreamAsync</c> stops working the instant its
///         <see cref="HttpResponseMessage" /> is disposed — reads return zero bytes or throw
///         <see cref="ObjectDisposedException" />. A <c>using</c> on the response, correct everywhere
///         else, is therefore fatal when the stream outlives the call that produced it: the failure
///         surfaces in the consumer, far from the code that caused it.
///     </para>
///     <para>
///         Wrapping the two together makes the lifetime a single object: whoever disposes the
///         <see cref="FileResponse" /> (and with it <see cref="FileResponse.Content" />) releases the
///         connection too. Nothing else may dispose the response.
///     </para>
/// </remarks>
public sealed class HttpResponseOwningStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    /// <summary>
    ///     Creates a stream that reads from <paramref name="inner" /> and owns <paramref name="response" />.
    /// </summary>
    /// <param name="inner">The content stream obtained from <paramref name="response" />.</param>
    /// <param name="response">The response whose lifetime is bound to this stream.</param>
    public HttpResponseOwningStream(Stream inner, HttpResponseMessage response)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(inner);
        Pragmatic.Ensure.Ensure.ThrowIfNull(response);

        _inner = inner;
        _response = response;
    }

    /// <inheritdoc />
    public override bool CanRead => _inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void CopyTo(Stream destination, int bufferSize) => _inner.CopyTo(destination, bufferSize);

    /// <inheritdoc />
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => _inner.CopyToAsync(destination, bufferSize, cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        if (disposing)
        {
            _inner.Dispose();
            _response.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _inner.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();

        GC.SuppressFinalize(this);
    }
}
