namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     The variable-length LZW of a GIF frame (least significant bit first, codes up to 12 bits), decoded the way
///     weezl decodes it for the gif crate, which the native library ran. The table starts with the literals, so a
///     stream need not open with a clear code. A code past the next one to be defined is an error, and so is the
///     next one itself where there is no code before it. A full table of 4,096 entries stops growing. Decoding stops
///     once the frame has its pixels, so what follows them is not read.
/// </summary>
internal ref struct GifLzwDecoder
{
    private const int MaxEntries = 4096;
    private const int MaxCodeSize = 12;

    private readonly GifSubBlocks _input;
    private readonly int _minimumSize;
    private readonly int _clear;
    private readonly int _end;
    private readonly Span<short> _prefix;
    private readonly Span<byte> _suffix;
    private readonly Span<byte> _first;
    private readonly Span<short> _length;
    private readonly Span<byte> _word;

    /// <param name="input">The frame's sub-blocks, after the minimum code size.</param>
    /// <param name="minimumSize">The minimum code size, 1 to 11.</param>
    /// <param name="table">Scratch for the code table: <see cref="TableBytes" /> bytes.</param>
    public GifLzwDecoder(GifSubBlocks input, int minimumSize, Span<byte> table)
    {
        _input = input;
        _minimumSize = minimumSize;
        _clear = 1 << minimumSize;
        _end = _clear + 1;
        _prefix = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(table[..(MaxEntries * 2)]);
        _length = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(table.Slice(MaxEntries * 2, MaxEntries * 2));
        _suffix = table.Slice(MaxEntries * 4, MaxEntries);
        _first = table.Slice(MaxEntries * 5, MaxEntries);
        _word = table.Slice(MaxEntries * 6, MaxEntries + 1);
    }

    /// <summary>The scratch the code table takes, and a word that does not fit the output.</summary>
    public const int TableBytes = MaxEntries * 7 + 1;

    /// <summary>Fills <paramref name="output" /> with palette indices.</summary>
    /// <exception cref="ImageDecodeException">The stream ends, or has an invalid code, before the frame is whole.</exception>
    public void Decode(Span<byte> output)
    {
        for (var code = 0; code < _clear; code++)
        {
            _prefix[code] = -1;
            _suffix[code] = (byte)code;
            _first[code] = (byte)code;
            _length[code] = 1;
        }

        var codeSize = _minimumSize + 1;
        var next = _end + 1;
        var previous = -1;
        var written = 0;
        var input = _input;
        uint bits = 0;
        var held = 0;
        while (written < output.Length)
        {
            while (held < codeSize)
            {
                var b = input.Next();
                if (b < 0)
                    throw Truncated();
                bits |= (uint)b << held;
                held += 8;
            }

            var code = (int)(bits & ((1u << codeSize) - 1));
            bits >>= codeSize;
            held -= codeSize;

            if (code == _clear)
            {
                codeSize = _minimumSize + 1;
                next = _end + 1;
                previous = -1;
                continue;
            }

            if (code == _end)
                throw Truncated();
            if (code > next || (code == next && previous < 0))
                throw new ImageDecodeException("GIF: the LZW data has an invalid code.");

            int first;
            if (code == next)
            {
                // The code being defined: the previous word and its own first byte.
                first = _first[previous];
                written = Emit(previous, output, written, extra: first);
            }
            else
            {
                first = _first[code];
                written = Emit(code, output, written, extra: -1);
            }

            if (previous >= 0 && next < MaxEntries)
            {
                _prefix[next] = (short)previous;
                _suffix[next] = (byte)first;
                _first[next] = _first[previous];
                _length[next] = (short)(_length[previous] + 1);
                if (next == (1 << codeSize) - 1 && codeSize < MaxCodeSize)
                    codeSize++;
                next++;
            }

            previous = code;
        }
    }

    /// <summary>Writes the word of <paramref name="code" />, then <paramref name="extra" /> if it is a byte, as far as the output reaches.</summary>
    private int Emit(int code, Span<byte> output, int written, int extra)
    {
        int length = _length[code];
        var total = extra >= 0 ? length + 1 : length;
        var room = output.Length - written;

        // The table holds a word last byte first: it is written back from its end, into the output when it fits,
        // or into the scratch word when only its start does.
        var target = total <= room ? output.Slice(written, total) : _word[..total];
        if (extra >= 0)
            target[length] = (byte)extra;
        var at = length - 1;
        for (var c = code; c >= 0; c = _prefix[c])
            target[at--] = _suffix[c];
        if (total <= room)
            return written + total;
        target[..room].CopyTo(output[written..]);
        return output.Length;
    }

    private static ImageDecodeException Truncated() => new("GIF: the image data is truncated.");
}
