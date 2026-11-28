using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Bytes already decoded, or stored as they are: an uncompressed strip is read from the file at its offset for as
///     long as the reader asks, past its byte count, as the TIFF crate read it.
/// </summary>
internal sealed class TiffMemorySource : ITiffChunkSource
{
    private ReadOnlyMemory<byte> _bytes;
    private byte[]? _rented;
    private int _position;

    /// <param name="bytes">What the reader reads.</param>
    /// <param name="rented">The pooled array behind them, returned when the chunk is done, if they are in one.</param>
    public TiffMemorySource Start(ReadOnlyMemory<byte> bytes, byte[]? rented = null)
    {
        _bytes = bytes;
        _rented = rented;
        _position = 0;
        return this;
    }

    public int Read(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _bytes.Length - _position);
        _bytes.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public void Dispose()
    {
        if (_rented is not null)
            ArrayPool<byte>.Shared.Return(_rented);
        _rented = null;
        _bytes = default;
    }
}
