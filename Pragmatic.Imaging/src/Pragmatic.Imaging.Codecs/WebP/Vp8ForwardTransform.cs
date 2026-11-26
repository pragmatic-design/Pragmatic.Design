// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     VP8's forward transforms, the inverses of <see cref="Vp8InverseTransform" />: the 4 x 4 integer DCT of
///     a block less its prediction, and the Walsh-Hadamard transform that gathers the DC of the 16 luma blocks
///     into the Y2 block. The arithmetic is the reference encoder's (libvpx's <c>vp8_short_fdct4x4_c</c>, as
///     libwebp writes it), whose rounding the inverse transforms are matched to.
/// </summary>
internal static class Vp8ForwardTransform
{
    /// <summary>
    ///     The DCT of <paramref name="source" /> less <paramref name="prediction" />, both 4 x 4 blocks at
    ///     <paramref name="offset" /> in buffers of <see cref="Vp8Prediction.Stride" />, into 16 coefficients in
    ///     raster order.
    /// </summary>
    public static void Dct(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int offset, Span<short> output)
    {
        if (Sse2.IsSupported)
            DctSse2(source, prediction, offset, output);
        else if (Vp8Transpose.IsAccelerated)
            DctVector(source, prediction, offset, output);
        else
            DctScalar(source, prediction, offset, output);
    }

    /// <summary>
    ///     The same arithmetic in 16-bit lanes, as libwebp's <c>FTransform_SSE2</c> does it: each product pair
    ///     of a pass (<c>a2 * 2217 + a3 * 5352</c> and the like) is one multiply-add into 32 bits, packed back
    ///     straight after. The block's differences are 9-bit and no intermediate leaves 16 bits.
    /// </summary>
    internal static void DctSse2(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int offset, Span<short> output)
    {
        const int s = Vp8Prediction.Stride;
        var end = offset + 3 * s + 4;
        if (source.Length < end || prediction.Length < end || output.Length < 16)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var src = ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset);
        ref var pred = ref Unsafe.Add(ref MemoryMarshal.GetReference(prediction), offset);

        // Rows 0 and 1, then 2 and 3, interleaved two pixels at a time: 00 01 10 11 02 03 12 13.
        var src01 = Sse2.UnpackLow(Row(ref src, 0), Row(ref src, s));
        var src23 = Sse2.UnpackLow(Row(ref src, 2 * s), Row(ref src, 3 * s));
        var pred01 = Sse2.UnpackLow(Row(ref pred, 0), Row(ref pred, s));
        var pred23 = Sse2.UnpackLow(Row(ref pred, 2 * s), Row(ref pred, 3 * s));
        TransformSse2(src01, src23, pred01, pred23, output);
    }

    /// <summary>
    ///     The same, the prediction given as its four row words (as <see cref="Vp8Prediction.Subblocks" /> gives
    ///     them) rather than read back from a buffer it was first written to.
    /// </summary>
    internal static void DctSse2(ReadOnlySpan<byte> source, int offset, ReadOnlySpan<uint> prediction, Span<short> output)
    {
        const int s = Vp8Prediction.Stride;
        if (source.Length < offset + 3 * s + 4 || prediction.Length < 4 || output.Length < 16)
            throw new ArgumentException("The block reaches past its buffer.");

        ref var src = ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset);
        ref var rows = ref MemoryMarshal.GetReference(prediction);
        var src01 = Sse2.UnpackLow(Row(ref src, 0), Row(ref src, s));
        var src23 = Sse2.UnpackLow(Row(ref src, 2 * s), Row(ref src, 3 * s));
        var pred01 = Sse2.UnpackLow(Word(rows), Word(Unsafe.Add(ref rows, 1)));
        var pred23 = Sse2.UnpackLow(Word(Unsafe.Add(ref rows, 2)), Word(Unsafe.Add(ref rows, 3)));
        TransformSse2(src01, src23, pred01, pred23, output);
    }

    /// <summary>The transform of the source rows less the prediction rows, each pair interleaved two pixels at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TransformSse2(Vector128<short> src01, Vector128<short> src23, Vector128<short> pred01, Vector128<short> pred23, Span<short> output)
    {
        var zero = Vector128<byte>.Zero;
        var row01 = Sse2.UnpackLow(src01.AsByte(), zero).AsInt16() - Sse2.UnpackLow(pred01.AsByte(), zero).AsInt16();
        var row23 = Sse2.UnpackLow(src23.AsByte(), zero).AsInt16() - Sse2.UnpackLow(pred23.AsByte(), zero).AsInt16();

        // First pass, across each row.
        var shuffled01 = Sse2.ShuffleHigh(row01, 0xB1);
        var shuffled23 = Sse2.ShuffleHigh(row23, 0xB1);
        var s01 = Sse2.UnpackLow(shuffled01.AsInt64(), shuffled23.AsInt64()).AsInt16();
        var s32 = Sse2.UnpackHigh(shuffled01.AsInt64(), shuffled23.AsInt64()).AsInt16();
        var a01 = s01 + s32;
        var a32 = s01 - s32;
        var t0 = Sse2.MultiplyAddAdjacent(a01, Vector128.Create((short)8, 8, 8, 8, 8, 8, 8, 8));
        var t2 = Sse2.MultiplyAddAdjacent(a01, Vector128.Create((short)8, -8, 8, -8, 8, -8, 8, -8));
        var t1 = Sse2.ShiftRightArithmetic(Sse2.MultiplyAddAdjacent(a32, Vector128.Create((short)5352, 2217, 5352, 2217, 5352, 2217, 5352, 2217)) + Vector128.Create(1812), 9);
        var t3 = Sse2.ShiftRightArithmetic(Sse2.MultiplyAddAdjacent(a32, Vector128.Create((short)2217, -5352, 2217, -5352, 2217, -5352, 2217, -5352)) + Vector128.Create(937), 9);
        var s03 = Sse2.PackSignedSaturate(t0, t2);
        var s12 = Sse2.PackSignedSaturate(t1, t3);
        var low = Sse2.UnpackLow(s03, s12);
        var high = Sse2.UnpackHigh(s03, s12);
        var v01 = Sse2.UnpackLow(low.AsInt32(), high.AsInt32()).AsInt16();
        var v32 = Sse2.Shuffle(Sse2.UnpackHigh(low.AsInt32(), high.AsInt32()), 0x4E).AsInt16();

        // Second pass, down each column.
        var b32 = v01 - v32;
        var b22 = Sse2.UnpackHigh(b32.AsInt64(), b32.AsInt64()).AsInt16();
        var b23 = Sse2.UnpackLow(b22, b32);
        var e1 = Sse2.ShiftRightArithmetic(Sse2.MultiplyAddAdjacent(b23, Vector128.Create((short)2217, 5352, 2217, 5352, 2217, 5352, 2217, 5352)) + Vector128.Create(12000 + (1 << 16)), 16);
        var e3 = Sse2.ShiftRightArithmetic(Sse2.MultiplyAddAdjacent(b23, Vector128.Create((short)-5352, 2217, -5352, 2217, -5352, 2217, -5352, 2217)) + Vector128.Create(51000), 16);
        var f1 = Sse2.PackSignedSaturate(e1, e1);
        var f3 = Sse2.PackSignedSaturate(e3, e3);

        // f1 + (a3 != 0): the one is added above, and the comparison takes it away again where a3 is zero.
        var g1 = f1 + Sse2.CompareEqual(b32, Vector128<short>.Zero);
        var b01 = v01 + v32;
        var b01Plus7 = b01 + Vector128.Create((short)7);
        var b11 = Sse2.UnpackHigh(b01.AsInt64(), b01.AsInt64()).AsInt16();
        var d0 = Sse2.ShiftRightArithmetic(b01Plus7 + b11, 4);
        var d2 = Sse2.ShiftRightArithmetic(b01Plus7 - b11, 4);

        ref var target = ref MemoryMarshal.GetReference(output);
        Sse2.UnpackLow(d0.AsInt64(), g1.AsInt64()).AsInt16().StoreUnsafe(ref target);
        Sse2.UnpackLow(d2.AsInt64(), f3.AsInt64()).AsInt16().StoreUnsafe(ref target, 8);
    }

    /// <summary>A row's four bytes, in the low lanes.</summary>
    private static Vector128<short> Row(ref byte block, int at) =>
        Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, at))).AsInt16();

    /// <summary>A row word's four bytes, in the low lanes.</summary>
    private static Vector128<short> Word(uint row) => Vector128.CreateScalar(row).AsInt16();

    /// <summary>
    ///     The same arithmetic four lanes at a time: the block is loaded column by column, so the first pass
    ///     combines whole vectors, and transposed once between the passes.
    /// </summary>
    internal static void DctVector(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int offset, Span<short> output)
    {
        const int s = Vp8Prediction.Stride;
        var end = offset + 3 * s + 4;
        if (source.Length < end || prediction.Length < end || output.Length < 16)
            throw new ArgumentException("The block reaches past its buffer.");

        var columns = Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15);
        var sourceBytes = Vector128.Shuffle(Rows(ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset)), columns);
        var predictionBytes = Vector128.Shuffle(Rows(ref Unsafe.Add(ref MemoryMarshal.GetReference(prediction), offset)), columns);
        var low = Vector128.WidenLower(sourceBytes).AsInt16() - Vector128.WidenLower(predictionBytes).AsInt16();
        var high = Vector128.WidenUpper(sourceBytes).AsInt16() - Vector128.WidenUpper(predictionBytes).AsInt16();
        var c0 = Vector128.WidenLower(low);
        var c1 = Vector128.WidenUpper(low);
        var c2 = Vector128.WidenLower(high);
        var c3 = Vector128.WidenUpper(high);

        // First pass, each lane one row of the block.
        var a0 = c0 + c3;
        var a1 = c1 + c2;
        var a2 = c1 - c2;
        var a3 = c0 - c3;
        var t0 = (a0 + a1) << 3;
        var t1 = Vector128.ShiftRightArithmetic(a2 * 2217 + a3 * 5352 + Vector128.Create(1812), 9);
        var t2 = (a0 - a1) << 3;
        var t3 = Vector128.ShiftRightArithmetic(a3 * 2217 - a2 * 5352 + Vector128.Create(937), 9);

        // Second pass, each lane one column.
        Vp8Transpose.Apply(ref t0, ref t1, ref t2, ref t3);
        a0 = t0 + t3;
        a1 = t1 + t2;
        a2 = t1 - t2;
        a3 = t0 - t3;
        var seven = Vector128.Create(7);
        var o0 = Vector128.ShiftRightArithmetic(a0 + a1 + seven, 4);
        var o1 = Vector128.ShiftRightArithmetic(a2 * 2217 + a3 * 5352 + Vector128.Create(12000), 16) +
                 (Vector128<int>.One + Vector128.Equals(a3, Vector128<int>.Zero));
        var o2 = Vector128.ShiftRightArithmetic(a0 - a1 + seven, 4);
        var o3 = Vector128.ShiftRightArithmetic(a3 * 2217 - a2 * 5352 + Vector128.Create(51000), 16);

        ref var target = ref MemoryMarshal.GetReference(output);
        Vector128.Narrow(o0, o1).StoreUnsafe(ref target);
        Vector128.Narrow(o2, o3).StoreUnsafe(ref target, 8);
    }

    /// <summary>The four rows of four bytes of a block in a work buffer, one after the other.</summary>
    private static Vector128<byte> Rows(ref byte block)
    {
        const int s = Vp8Prediction.Stride;
        return Vector128.Create(
            Unsafe.ReadUnaligned<uint>(ref block),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, s)),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, 2 * s)),
            Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref block, 3 * s))).AsByte();
    }

    [SkipLocalsInit]
    internal static void DctScalar(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int offset, Span<short> output)
    {
        const int s = Vp8Prediction.Stride;
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            var row = offset + i * s;
            var d0 = source[row] - prediction[row];
            var d1 = source[row + 1] - prediction[row + 1];
            var d2 = source[row + 2] - prediction[row + 2];
            var d3 = source[row + 3] - prediction[row + 3];
            var a0 = d0 + d3;
            var a1 = d1 + d2;
            var a2 = d1 - d2;
            var a3 = d0 - d3;
            temp[i * 4] = (a0 + a1) * 8;
            temp[i * 4 + 1] = (a2 * 2217 + a3 * 5352 + 1812) >> 9;
            temp[i * 4 + 2] = (a0 - a1) * 8;
            temp[i * 4 + 3] = (a3 * 2217 - a2 * 5352 + 937) >> 9;
        }

        for (var i = 0; i < 4; i++)
        {
            var a0 = temp[i] + temp[12 + i];
            var a1 = temp[4 + i] + temp[8 + i];
            var a2 = temp[4 + i] - temp[8 + i];
            var a3 = temp[i] - temp[12 + i];
            output[i] = (short)((a0 + a1 + 7) >> 4);
            output[4 + i] = (short)(((a2 * 2217 + a3 * 5352 + 12000) >> 16) + (a3 != 0 ? 1 : 0));
            output[8 + i] = (short)((a0 - a1 + 7) >> 4);
            output[12 + i] = (short)((a3 * 2217 - a2 * 5352 + 51000) >> 16);
        }
    }

    /// <summary>
    ///     The Walsh-Hadamard transform of the DC of the 16 luma blocks (coefficient 0 of each 16, raster order
    ///     of blocks) into the Y2 block, raster order.
    /// </summary>
    [SkipLocalsInit]
    public static void Walsh(ReadOnlySpan<short> blocks, Span<short> output)
    {
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            var row = i * 64;
            var a0 = blocks[row] + blocks[row + 32];
            var a1 = blocks[row + 16] + blocks[row + 48];
            var a2 = blocks[row + 16] - blocks[row + 48];
            var a3 = blocks[row] - blocks[row + 32];
            temp[i * 4] = a0 + a1;
            temp[i * 4 + 1] = a3 + a2;
            temp[i * 4 + 2] = a3 - a2;
            temp[i * 4 + 3] = a0 - a1;
        }

        for (var i = 0; i < 4; i++)
        {
            var a0 = temp[i] + temp[8 + i];
            var a1 = temp[4 + i] + temp[12 + i];
            var a2 = temp[4 + i] - temp[12 + i];
            var a3 = temp[i] - temp[8 + i];
            output[i] = (short)((a0 + a1) >> 1);
            output[4 + i] = (short)((a3 + a2) >> 1);
            output[8 + i] = (short)((a3 - a2) >> 1);
            output[12 + i] = (short)((a0 - a1) >> 1);
        }
    }
}
