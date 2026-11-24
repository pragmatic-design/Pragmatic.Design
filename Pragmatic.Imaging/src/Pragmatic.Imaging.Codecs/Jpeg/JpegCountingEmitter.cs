using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Pragmatic.Imaging.Codecs.Jpeg.JpegCoefficientCoding;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     The first pass of optimised coding: the block's symbols into a compact stream, counted per table,
///     so that the Huffman tables can be fitted before anything is written.
/// </summary>
internal struct JpegCountingEmitter : IJpegBlockEmitter
{
    /// <summary>
    ///     One entry of the symbol stream. Kind: bit 15 the table's component (0 luma, 1 chroma), bit 14
    ///     its class (0 DC, 1 AC), bits 8..11 the number of magnitude bits, bits 0..7 the symbol.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct Symbol
    {
        public ushort Kind;
        public ushort Magnitude;
    }

    private readonly Symbol[] _stream;
    private readonly JpegHuffmanBuilder _dcLuma;
    private readonly JpegHuffmanBuilder _acLuma;
    private readonly JpegHuffmanBuilder _dcChroma;
    private readonly JpegHuffmanBuilder _acChroma;

    public JpegCountingEmitter(Symbol[] stream, JpegHuffmanBuilder dcLuma, JpegHuffmanBuilder acLuma, JpegHuffmanBuilder dcChroma, JpegHuffmanBuilder acChroma)
    {
        _stream = stream;
        _dcLuma = dcLuma;
        _acLuma = acLuma;
        _dcChroma = dcChroma;
        _acChroma = acChroma;
        Count = 0;
    }

    /// <summary>How many symbols the stream holds.</summary>
    public int Count { get; private set; }

    public void Emit(ReadOnlySpan<short> block, ulong zigzagMask, ref int predictor, int component)
    {
        var dc = component == 0 ? _dcLuma : _dcChroma;
        var ac = component == 0 ? _acLuma : _acChroma;
        var kindBase = component << 15;
        ref var zigzag = ref MemoryMarshal.GetArrayDataReference(ZigzagTransposed);
        ref var coefficients = ref MemoryMarshal.GetReference(block);
        ref var symbols = ref MemoryMarshal.GetArrayDataReference(_stream);
        var count = Count;

        var dcValue = coefficients;
        var diff = dcValue - predictor;
        predictor = dcValue;
        var category = Category(diff);
        dc.Count(category);
        ref var entry = ref Unsafe.Add(ref symbols, count++);
        entry.Kind = (ushort)(kindBase | (category << 8) | category);
        entry.Magnitude = (ushort)Magnitude(diff, category);

        var acBase = kindBase | 0x4000;
        var remaining = zigzagMask >> 1; // bit k − 1 stands for zigzag position k
        var previous = 0;
        while (remaining != 0)
        {
            var k = System.Numerics.BitOperations.TrailingZeroCount(remaining) + 1;
            remaining &= remaining - 1;
            var run = k - previous - 1;
            previous = k;

            while (run > 15)
            {
                ac.Count(0xF0);
                ref var zrl = ref Unsafe.Add(ref symbols, count++);
                zrl.Kind = (ushort)(acBase | 0xF0);
                zrl.Magnitude = 0;
                run -= 16;
            }

            int value = Unsafe.Add(ref coefficients, Unsafe.Add(ref zigzag, k));
            var size = Category(value);
            var symbol = (run << 4) | size;
            ac.Count(symbol);
            ref var e = ref Unsafe.Add(ref symbols, count++);
            e.Kind = (ushort)(acBase | (size << 8) | symbol);
            e.Magnitude = (ushort)Magnitude(value, size);
        }

        if (previous < 63)
        {
            ac.Count(0x00);
            ref var eob = ref Unsafe.Add(ref symbols, count++);
            eob.Kind = (ushort)acBase;
            eob.Magnitude = 0;
        }

        Count = count;
    }

    /// <summary>The stream through the fitted tables, flattened as <see cref="JpegEncodingTable.Flatten" /> lays them out: one lookup and one append per symbol.</summary>
    public readonly void WriteStream(JpegBitWriter writer, ReadOnlySpan<uint> codes)
    {
        ref var table = ref MemoryMarshal.GetReference(codes);
        ref var symbols = ref MemoryMarshal.GetArrayDataReference(_stream);
        for (var i = 0; i < Count; i++)
        {
            ref var entry = ref Unsafe.Add(ref symbols, i);
            var kind = entry.Kind;
            var code = Unsafe.Add(ref table, ((kind >> 6) & 0x300) | (kind & 0xFF));
            System.Diagnostics.Debug.Assert(code >> 16 != 0, "a symbol in the stream has no code");
            var size = (kind >> 8) & 15;
            // A code is at most 16 bits and a magnitude at most 11: the two fit one 32-bit append.
            writer.Write((int)(((code & 0xFFFF) << size) | entry.Magnitude), (int)(code >> 16) + size);
        }
    }
}
