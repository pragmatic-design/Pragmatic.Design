using Pragmatic.Imaging.Codecs.Ccitt;

namespace Pragmatic.Imaging.Codecs.Tiff;

/// <summary>
///     A Group 4 strip, a line at a time and only as many lines as the reader asks for, as the TIFF crate's reader
///     decoded it: at most the chunk's rows, black as 1, an end of block ending the data.
/// </summary>
internal sealed class TiffFax4Source : ITiffChunkSource
{
    private readonly CcittDecoder _decoder = new();
    private byte[] _line = new byte[256];
    private int _lineLength;
    private int _height;
    private int _y;
    private int _at;
    private int _filled;

    /// <exception cref="ImageDecodeException">The chunk is wider or taller than 65,535 pixels, which the reader refused.</exception>
    public TiffFax4Source Start(ReadOnlyMemory<byte> input, long width, long height)
    {
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ImageDecodeException("TIFF: a Group 4 strip larger than 65,535 pixels a side is not read.");
        _height = (int)height;
        _lineLength = (int)((width + 7) / 8);
        if (_line.Length < _lineLength)
            _line = new byte[_lineLength];
        _y = _at = _filled = 0;
        _decoder.Start(input, (int)width, -1, false);
        return this;
    }

    public int Read(Span<byte> buffer)
    {
        if (_at == _filled && _y < _height && _decoder.ReadLine(_line))
        {
            _y++;
            _at = 0;
            _filled = _lineLength;
        }

        var count = Math.Min(buffer.Length, _filled - _at);
        _line.AsSpan(_at, count).CopyTo(buffer);
        _at += count;
        return count;
    }

    public void Dispose() => _decoder.Start(default, 1, -1, false);
}
