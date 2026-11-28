using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.Bmp;

/// <summary>
///     One channel of a 16- or 32-bit BMP pixel: where its bits are and how many. A channel wider than 8 bits
///     keeps its top 8; a narrower one is scaled to 0..255 by the tables the image crate uses, so a value
///     reads as the native library read it.
/// </summary>
internal readonly record struct BmpBitfield(int Shift, int Length)
{
    private static ReadOnlySpan<byte> ThreeBits => [0, 36, 73, 109, 146, 182, 219, 255];

    private static ReadOnlySpan<byte> FourBits => [0, 17, 34, 51, 68, 85, 102, 119, 136, 153, 170, 187, 204, 221, 238, 255];

    private static ReadOnlySpan<byte> FiveBits =>
    [
        0, 8, 16, 25, 33, 41, 49, 58, 66, 74, 82, 90, 99, 107, 115, 123, 132, 140, 148, 156, 165, 173, 181, 189, 197, 206, 214,
        222, 230, 239, 247, 255,
    ];

    private static ReadOnlySpan<byte> SixBits =>
    [
        0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 45, 49, 53, 57, 61, 65, 69, 73, 77, 81, 85, 89, 93, 97, 101, 105, 109, 113, 117,
        121, 125, 130, 134, 138, 142, 146, 150, 154, 158, 162, 166, 170, 174, 178, 182, 186, 190, 194, 198, 202, 206, 210, 215, 219,
        223, 227, 231, 235, 239, 243, 247, 251, 255,
    ];

    /// <summary>The channel a mask selects, or none for a zero mask, refusing one that is not contiguous or does not fit.</summary>
    /// <exception cref="ImageDecodeException">The mask's bits are not contiguous, or reach past <paramref name="bits" />.</exception>
    public static BmpBitfield FromMask(uint mask, int bits)
    {
        if (mask == 0)
            return default;
        var shift = BitOperations.TrailingZeroCount(mask);
        var length = BitOperations.TrailingZeroCount(~(mask >> shift));
        if (length != BitOperations.PopCount(mask))
            throw new ImageDecodeException("BMP: a bitfield mask is not contiguous.");
        if (length + shift > bits)
            throw new ImageDecodeException("BMP: a bitfield mask does not fit the pixel.");
        return length > 8 ? new BmpBitfield(shift + length - 8, 8) : new BmpBitfield(shift, length);
    }

    public bool IsPresent => Length != 0;

    /// <summary>The channel of <paramref name="pixel" />, from 0 to 255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read(uint pixel)
    {
        var value = pixel >> Shift;
        return Length switch
        {
            1 => (byte)((value & 1) * 0xFF),
            2 => (byte)((value & 3) * 0x55),
            3 => ThreeBits[(int)(value & 7)],
            4 => FourBits[(int)(value & 15)],
            5 => FiveBits[(int)(value & 31)],
            6 => SixBits[(int)(value & 63)],
            7 => (byte)(((value & 0x7F) << 1) | ((value & 0x7F) >> 6)),
            _ => (byte)value,
        };
    }
}
