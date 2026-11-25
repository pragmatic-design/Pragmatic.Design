namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     A lossless WebP written bit by bit (RFC 9649): two pixels, a colour-indexing transform with a
///     one-colour table, and a packed pixel whose second index is 1, past the table. The format makes such
///     a pixel transparent black; the first pixel is the table's colour.
/// </summary>
internal static class IndexPastThePalette
{
    /// <summary>The table's one colour, as RGBA.</summary>
    public static readonly byte[] Colour = [0x10, 0x20, 0x30, 0xFF];

    public static byte[] File()
    {
        var bits = new LsbBits();
        bits.Write(0x2F, 8); // signature
        bits.Write(2 - 1, 14); // width
        bits.Write(1 - 1, 14); // height
        bits.Write(1, 1); // alpha is used
        bits.Write(0, 3); // version

        bits.Write(1, 1); // a transform follows
        bits.Write(3, 2); // colour indexing
        bits.Write(1 - 1, 8); // one colour: eight indices per packed pixel
        bits.Write(0, 1); // the table: no colour cache
        Single(bits, Colour[1]); // green
        Single(bits, Colour[0]); // red
        Single(bits, Colour[2]); // blue
        Single(bits, Colour[3]); // alpha
        Single(bits, 0); // distance

        bits.Write(0, 1); // no more transforms
        bits.Write(0, 1); // the image: no colour cache
        bits.Write(0, 1); // one group of codes
        Single(bits, 0b10); // green: index 0 for the first pixel, 1 for the second
        Single(bits, 0);
        Single(bits, 0);
        Single(bits, 0);
        Single(bits, 0);

        var payload = bits.ToArray();
        var riff = new List<byte>();
        riff.AddRange("RIFF"u8.ToArray());
        riff.AddRange(BitConverter.GetBytes(4 + 8 + payload.Length + (payload.Length & 1)));
        riff.AddRange("WEBPVP8L"u8.ToArray());
        riff.AddRange(BitConverter.GetBytes(payload.Length));
        riff.AddRange(payload);
        if ((payload.Length & 1) == 1)
            riff.Add(0);
        return [.. riff];
    }

    /// <summary>A simple prefix code of one 8-bit symbol, which then takes no bits to read.</summary>
    private static void Single(LsbBits bits, int symbol)
    {
        bits.Write(1, 1); // simple code
        bits.Write(0, 1); // one symbol
        bits.Write(1, 1); // eight bits wide
        bits.Write(symbol, 8);
    }

    private sealed class LsbBits
    {
        private readonly List<byte> _bytes = [];
        private int _used;

        public void Write(int value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (_used == 0)
                    _bytes.Add(0);
                _bytes[^1] |= (byte)(((value >> i) & 1) << _used);
                _used = (_used + 1) & 7;
            }
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
