namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     Apple's PackBits, as the TIFF crate's reader unpacks it: a header byte of 0 to 127 starts a literal of one more
///     byte, -1 to -127 repeats the next byte one minus it times, -128 is nothing. A header that promises a byte the
///     strip does not have is an error; a literal the strip cuts short ends the data.
/// </summary>
internal sealed class TiffPackBitsSource : ITiffChunkSource
{
    private ReadOnlyMemory<byte> _input;
    private int _position;
    private int _count;
    private int _repeat;
    private bool _literal;

    /// <param name="input">The strip's bytes: the file from the strip's offset, at most its byte count long.</param>
    public TiffPackBitsSource Start(ReadOnlyMemory<byte> input)
    {
        _input = input;
        _position = _count = 0;
        _repeat = -1;
        _literal = false;
        return this;
    }

    public int Read(Span<byte> buffer)
    {
        var input = _input.Span;
        while (_count == 0)
        {
            if (_position >= input.Length)
                return 0;
            var header = (sbyte)input[_position++];
            if (header < 0 && header != -128)
            {
                if (_position >= input.Length)
                    throw new ImageDecodeException("TIFF: the PackBits data ends inside a run.");
                _repeat = input[_position++];
                _literal = false;
                _count = 1 - header;
            }
            else if (header >= 0)
            {
                _literal = true;
                _count = header + 1;
            }
        }

        var length = Math.Min(buffer.Length, _count);
        if (_literal)
        {
            length = Math.Min(length, input.Length - _position);
            input.Slice(_position, length).CopyTo(buffer);
            _position += length;
        }
        else
        {
            buffer[..length].Fill((byte)_repeat);
        }

        _count -= length;
        return length;
    }

    public void Dispose() => _input = default;
}
