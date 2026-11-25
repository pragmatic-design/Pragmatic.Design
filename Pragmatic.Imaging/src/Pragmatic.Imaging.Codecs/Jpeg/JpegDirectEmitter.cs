using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Pragmatic.Imaging.Codecs.Jpeg.JpegCoefficientCoding;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     Single-pass coding through tables known in advance (the standard ones): each block's symbols go
///     straight to the bit writer, nothing is counted or stored.
/// </summary>
internal readonly struct JpegDirectEmitter : IJpegBlockEmitter
{
    private readonly JpegBitWriter _writer;
    private readonly uint[] _codes;

    /// <param name="writer">Where the bits go.</param>
    /// <param name="codes">The four tables flattened as <see cref="JpegEncodingTable.Flatten" /> lays them out.</param>
    public JpegDirectEmitter(JpegBitWriter writer, uint[] codes)
    {
        _writer = writer;
        _codes = codes;
    }

    public void Emit(ReadOnlySpan<short> block, ulong zigzagMask, ref int predictor, int component)
    {
        var writer = _writer;
        ref var codes = ref MemoryMarshal.GetArrayDataReference(_codes);
        ref var zigzag = ref MemoryMarshal.GetArrayDataReference(ZigzagTransposed);
        ref var coefficients = ref MemoryMarshal.GetReference(block);
        var dcTable = component << 9; // (component, class) → table index × 256
        var acTable = dcTable | 256;

        var dcValue = coefficients;
        var diff = dcValue - predictor;
        predictor = dcValue;
        var category = Category(diff);
        WriteSymbol(writer, Unsafe.Add(ref codes, dcTable | category), Magnitude(diff, category), category);

        var remaining = zigzagMask >> 1;
        var previous = 0;
        while (remaining != 0)
        {
            var k = System.Numerics.BitOperations.TrailingZeroCount(remaining) + 1;
            remaining &= remaining - 1;
            var run = k - previous - 1;
            previous = k;

            while (run > 15)
            {
                WriteSymbol(writer, Unsafe.Add(ref codes, acTable | 0xF0), 0, 0);
                run -= 16;
            }

            int value = Unsafe.Add(ref coefficients, Unsafe.Add(ref zigzag, k));
            var size = Category(value);
            WriteSymbol(writer, Unsafe.Add(ref codes, acTable | (run << 4) | size), Magnitude(value, size), size);
        }

        if (previous < 63)
            WriteSymbol(writer, Unsafe.Add(ref codes, acTable), 0, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSymbol(JpegBitWriter writer, uint code, int magnitude, int size)
    {
        System.Diagnostics.Debug.Assert(code >> 16 != 0, "the table has no code for this symbol");
        writer.Write((int)(((code & 0xFFFF) << size) | (uint)magnitude), (int)(code >> 16) + size);
    }
}
