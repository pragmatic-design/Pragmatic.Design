namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     The bytes of a chain of GIF data sub-blocks, each a length byte and that many bytes, ending at a zero
///     length. Read lazily, as the gif crate reads them: a file that ends where a byte is still wanted is an error.
/// </summary>
internal ref struct GifSubBlocks(ReadOnlySpan<byte> data, int position)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position = position;
    private int _left;
    private bool _ended;

    /// <summary>The next byte, or -1 at the chain's terminator.</summary>
    /// <exception cref="ImageDecodeException">The file ends inside the chain.</exception>
    public int Next()
    {
        while (_left == 0)
        {
            if (_ended)
                return -1;
            if (_position >= _data.Length)
                throw new ImageDecodeException("GIF: the file ends inside the image data.");
            _left = _data[_position++];
            if (_left == 0)
            {
                _ended = true;
                return -1;
            }
        }

        if (_position >= _data.Length)
            throw new ImageDecodeException("GIF: the file ends inside the image data.");
        _left--;
        return _data[_position++];
    }
}
