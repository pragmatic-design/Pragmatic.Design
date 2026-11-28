using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     A GIF file being written, in a buffer from the shared pool that grows as it fills; image data goes through
///     <see cref="Data" /> and comes out in sub-blocks of 255 bytes and a shorter last one. Dispose it to give the
///     buffer back.
/// </summary>
internal sealed class GifOutput : IDisposable
{
    private const int BlockSize = 255;

    private byte[] _buffer;
    private int _length;
    private int _blockStart = -1;

    public GifOutput(int capacity) => _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 1024));

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    public void Byte(byte value)
    {
        Reserve(1);
        _buffer[_length++] = value;
    }

    public void Bytes(ReadOnlySpan<byte> values)
    {
        Reserve(values.Length);
        values.CopyTo(_buffer.AsSpan(_length));
        _length += values.Length;
    }

    /// <summary>One byte of image data, in the current sub-block.</summary>
    public void Data(byte value)
    {
        if (_blockStart < 0 || _length - _blockStart - 1 == BlockSize)
        {
            Reserve(1);
            _blockStart = _length;
            _buffer[_length++] = BlockSize;
        }

        Reserve(1);
        _buffer[_length++] = value;
    }

    /// <summary>Closes the image data: the last sub-block's length, and the terminator.</summary>
    public void EndData()
    {
        if (_blockStart >= 0)
            _buffer[_blockStart] = (byte)(_length - _blockStart - 1);
        _blockStart = -1;
        Byte(0);
    }

    public void Dispose()
    {
        if (_buffer.Length == 0)
            return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private void Reserve(int count)
    {
        if (_length + count <= _buffer.Length)
            return;
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + count));
        _buffer.AsSpan(0, _length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }
}
