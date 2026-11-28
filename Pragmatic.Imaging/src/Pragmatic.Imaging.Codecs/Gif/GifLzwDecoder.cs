using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Gif;

/// <summary>
///     The variable-length LZW of a GIF frame (least significant bit first, codes up to 12 bits), decoded the way
///     weezl decodes it for the gif crate, which the native library ran. The table starts with the literals, so a
///     stream need not open with a clear code. A code past the next one to be defined is an error, and so is the
///     next one itself where there is no code before it. A full table of 4,096 entries stops growing. Decoding stops
///     once the frame has its pixels, so what follows them is not read.
/// </summary>
/// <remarks>
///     Every word in the table has already been written to the output once: an entry is the previous word and the
///     byte that follows it there. So an entry is kept as where that copy starts and how long it is, and a word is
///     one copy from earlier in the output, instead of a walk down a chain of prefixes a byte at a time.
/// </remarks>
internal ref struct GifLzwDecoder
{
    private const int MaxEntries = 4096;
    private const int MaxCodeSize = 12;

    private readonly GifSubBlocks _input;
    private readonly int _minimumSize;
    private readonly Span<int> _start;
    private readonly Span<short> _length;

    /// <param name="input">The frame's sub-blocks, after the minimum code size.</param>
    /// <param name="minimumSize">The minimum code size, 1 to 11.</param>
    /// <param name="table">Scratch for the code table: <see cref="TableBytes" /> bytes.</param>
    public GifLzwDecoder(GifSubBlocks input, int minimumSize, Span<byte> table)
    {
        _input = input;
        _minimumSize = minimumSize;
        _start = MemoryMarshal.Cast<byte, int>(table[..(MaxEntries * 4)]);
        _length = MemoryMarshal.Cast<byte, short>(table.Slice(MaxEntries * 4, MaxEntries * 2));
    }

    /// <summary>The scratch the code table takes.</summary>
    public const int TableBytes = MaxEntries * 6;

    /// <summary>Fills <paramref name="output" /> with palette indices.</summary>
    /// <exception cref="ImageDecodeException">The stream ends, or has an invalid code, before the frame is whole.</exception>
    public void Decode(Span<byte> output)
    {
        var start = _start;
        var lengths = _length;
        var clear = 1 << _minimumSize;
        var end = clear + 1;
        var codeSize = _minimumSize + 1;
        var next = end + 1;
        var previous = -1;
        var previousStart = 0;
        var previousLength = 0;
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

            if (code == clear)
            {
                codeSize = _minimumSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
                throw Truncated();
            if (code > next || (code == next && previous < 0))
                throw new ImageDecodeException("GIF: the LZW data has an invalid code.");

            var at = written;
            int length;
            var room = output.Length - written;
            if (code < clear)
            {
                length = 1;
                output[written++] = (byte)code;
            }
            else if (code == next)
            {
                // The code being defined: the previous word, which ends where this one starts, and its first byte.
                length = previousLength + 1;
                var copied = Math.Min(previousLength, room);
                Copy(output, previousStart, written, copied);
                if (copied < room)
                    output[written + previousLength] = output[previousStart];
                written += Math.Min(length, room);
            }
            else
            {
                length = lengths[code];
                var copied = Math.Min(length, room);
                Copy(output, start[code], written, copied);
                written += copied;
            }

            if (previous >= 0 && next < MaxEntries)
            {
                start[next] = previousStart;
                lengths[next] = (short)(previousLength + 1);
                if (next == (1 << codeSize) - 1 && codeSize < MaxCodeSize)
                    codeSize++;
                next++;
            }

            previous = code;
            previousStart = at;
            previousLength = length;
        }
    }

    // A word earlier in the output to its end; the two never overlap, since a word ends at or before the current one
    // starts. Most words are a few bytes, where a loop costs less than the call to a block copy. Inlined, so the span
    // is not passed through memory on each code, which System V stalls on.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Copy(Span<byte> output, int from, int to, int count)
    {
        if (count <= 8)
        {
            for (var i = 0; i < count; i++)
                output[to + i] = output[from + i];
            return;
        }

        output.Slice(from, count).CopyTo(output.Slice(to, count));
    }

    private static ImageDecodeException Truncated() => new("GIF: the image data is truncated.");
}
