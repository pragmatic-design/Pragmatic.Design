using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     What turning a quantised block into symbols needs, shared by the encoder's emitters. The block
///     is in the transposed layout of <see cref="JpegForwardDctAvx2" /> (coefficient of row v and column
///     u at index u·8 + v); the tables here read it in zigzag order.
/// </summary>
internal static class JpegCoefficientCoding
{
    /// <summary>Index u·8 + v of the transposed block that holds zigzag position k, whose natural index is v·8 + u.</summary>
    public static readonly byte[] ZigzagTransposed = BuildZigzagTransposed();

    /// <summary>Entry [i * 256 + b]: the zigzag-order bits of transposed-block indices 8i..8i+7 whose byte is b.</summary>
    private static readonly ulong[] ZigzagMaskTable = BuildZigzagMaskTable();

    /// <summary>The block-order non-zero mask permuted to zigzag order: one table lookup per byte of it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToZigzagOrder(ulong blockOrder)
    {
        ref var table = ref MemoryMarshal.GetArrayDataReference(ZigzagMaskTable);
        var result = 0UL;
        for (var i = 0; i < 8; i++)
            result |= Unsafe.Add(ref table, i * 256 + (int)((blockOrder >> (i * 8)) & 0xFF));
        return result;
    }

    /// <summary>The number of bits that hold |value|: the DC or AC "size" category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Category(int value) => 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Abs(value));

    /// <summary>The value's magnitude bits: itself when positive, its lower bits minus one when negative. No branch: the sign is a coin toss.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Magnitude(int value, int size)
    {
        var negative = value >> 31; // all ones when negative
        return value + (negative & ((1 << size) - 1));
    }

    /// <summary>The zigzag index of each natural position: the inverse of <see cref="JpegMarker.Zigzag" />.</summary>
    private static ReadOnlySpan<byte> InverseZigzag =>
    [
        0, 1, 5, 6, 14, 15, 27, 28,
        2, 4, 7, 13, 16, 26, 29, 42,
        3, 8, 12, 17, 25, 30, 41, 43,
        9, 11, 18, 24, 31, 40, 44, 53,
        10, 19, 23, 32, 39, 45, 52, 54,
        20, 22, 33, 38, 46, 51, 55, 60,
        21, 34, 37, 47, 50, 56, 59, 61,
        35, 36, 48, 49, 57, 58, 62, 63,
    ];

    private static byte[] BuildZigzagTransposed()
    {
        var zigzag = JpegMarker.Zigzag;
        var transposed = new byte[64];
        for (var k = 0; k < 64; k++)
            transposed[k] = (byte)(((zigzag[k] & 7) << 3) | (zigzag[k] >> 3));
        return transposed;
    }

    private static ulong[] BuildZigzagMaskTable()
    {
        var table = new ulong[8 * 256];
        var inverse = InverseZigzag;
        for (var i = 0; i < 8; i++)
        {
            for (var b = 0; b < 256; b++)
            {
                var bits = 0UL;
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((b & (1 << bit)) == 0)
                        continue;
                    var index = i * 8 + bit; // u·8 + v in the transposed block
                    var natural = ((index & 7) << 3) | (index >> 3); // v·8 + u
                    bits |= 1UL << inverse[natural];
                }

                table[i * 256 + b] = bits;
            }
        }

        return table;
    }
}
