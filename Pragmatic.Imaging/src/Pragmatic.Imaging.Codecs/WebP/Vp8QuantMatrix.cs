using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The quantization of one kind of block (luma, Y2 or chroma) in one segment: a step for the DC and one
///     for every AC coefficient, each divided by as a fixed-point reciprocal with a rounding bias, the
///     encoder's choice of rounding toward zero that the format leaves free. Luma AC is sharpened a little
///     toward the higher frequencies, as libwebp sharpens it.
/// </summary>
internal sealed class Vp8QuantMatrix
{
    public const int MaxLevel = 2047;
    private const int FixBits = 17;

    // The rounding biases out of 256, DC then AC, for luma, Y2 and chroma; and the sharpening per raster
    // position out of 2048 of the step. Both are libwebp's (quant_enc.c).
    private static ReadOnlySpan<byte> Biases => [96, 110, 96, 108, 110, 115];

    private static ReadOnlySpan<byte> Sharpening => [0, 30, 60, 90, 30, 60, 90, 90, 60, 90, 90, 90, 90, 90, 90, 90];

    // Sixteen values each, in raster order: the step, its reciprocal, the bias, the largest magnitude that
    // quantizes to zero, and the sharpening.
    private const int Step = 0;
    private const int Reciprocal = 16;
    private const int Bias = 32;
    private const int Threshold = 48;
    private const int Sharpen = 64;

    private readonly int[] _table = new int[5 * 16];

    // The same in 16 bits, for the path that works on all sixteen coefficients at once: step, reciprocal
    // (at most 2^15, so it fits unsigned), threshold and sharpening, raster order; and the bias in 32 bits,
    // in the order the products come out of an in-lane unpack: 0-3 and 8-11, then 4-7 and 12-15.
    private const int Step16 = 0;
    private const int Reciprocal16 = 16;
    private const int Threshold16 = 32;
    private const int Sharpen16 = 48;
    private readonly short[] _table16 = new short[4 * 16];
    private readonly int[] _unpackedBias = new int[16];

    /// <param name="dcStep">The DC step, as the decoder will multiply by it.</param>
    /// <param name="acStep">The AC step.</param>
    /// <param name="kind">0 luma, 1 Y2, 2 chroma.</param>
    public Vp8QuantMatrix(int dcStep, int acStep, int kind) => Set(dcStep, acStep, kind);

    /// <summary>Makes this the matrix of the given steps, whatever it was: an encoder reuses its matrices image after image.</summary>
    public void Set(int dcStep, int acStep, int kind)
    {
        for (var j = 0; j < 16; j++)
        {
            var step = j == 0 ? dcStep : acStep;
            var reciprocal = (1 << FixBits) / step;
            var bias = Biases[kind * 2 + (j == 0 ? 0 : 1)] << (FixBits - 8);
            _table[Step + j] = step;
            _table[Reciprocal + j] = reciprocal;
            _table[Bias + j] = bias;
            _table[Threshold + j] = ((1 << FixBits) - 1 - bias) / reciprocal;
            _table[Sharpen + j] = kind == 0 ? (Sharpening[j] * step) >> 11 : 0;
            _table16[Step16 + j] = (short)step;
            _table16[Reciprocal16 + j] = (short)(ushort)reciprocal;
            _table16[Threshold16 + j] = (short)_table[Threshold + j];
            _table16[Sharpen16 + j] = (short)_table[Sharpen + j];
            _unpackedBias[(j & 3) + ((j >> 3) << 2) + (((j >> 2) & 1) << 3)] = bias;
        }

        DcStep = dcStep;
        AcStep = acStep;
        AverageStep = (dcStep + 15 * acStep + 8) >> 4;
    }

    /// <summary>The DC step.</summary>
    public int DcStep { get; private set; }

    /// <summary>The AC step.</summary>
    public int AcStep { get; private set; }

    /// <summary>The mean of the 16 steps, which the rate-distortion weights are derived from.</summary>
    public int AverageStep { get; private set; }

    /// <summary>
    ///     Quantizes a DC coefficient in place, to what the decoder will rebuild, and returns half the error
    ///     made, signed: what the chroma DC error diffusion passes on to the next blocks.
    /// </summary>
    public int QuantizeDc(ref short value)
    {
        int v = value;
        var negative = v < 0;
        if (negative)
            v = -v;
        if (v > _table[Threshold])
        {
            var quantized = (int)(((uint)v * (uint)_table[Reciprocal] + (uint)_table[Bias]) >> FixBits) * _table[Step];
            var error = v - quantized;
            value = (short)(negative ? -quantized : quantized);
            return (negative ? -error : error) >> 1;
        }

        value = 0;
        return (negative ? -v : v) >> 1;
    }

    /// <summary>
    ///     Quantizes the 16 coefficients of <paramref name="coefficients" /> (raster order) from position
    ///     <paramref name="first" /> of the scan on, writing the levels to <paramref name="levels" /> in scan
    ///     order and replacing each coefficient by what the decoder will rebuild from its level. Returns whether
    ///     any level is not zero.
    /// </summary>
    public bool Quantize(Span<short> coefficients, Span<short> levels, int first = 0) =>
        Avx2.IsSupported ? QuantizeAvx2(coefficients, levels, first)
        : Vector128.IsHardwareAccelerated ? QuantizeVector(coefficients, levels, first)
        : QuantizeScalar(coefficients, levels, first);

    /// <summary>
    ///     The same, all sixteen coefficients in one vector and in 16 bits as libwebp's SSE2 quantizer works:
    ///     only the product of the magnitude and the reciprocal is taken to 32 bits. Exact for every VP8
    ///     coefficient, whose magnitude plus sharpening stays below 2^15 (at most 16,320, after the Walsh
    ///     transform of 16 DCs).
    /// </summary>
    internal bool QuantizeAvx2(Span<short> coefficients, Span<short> levels, int first = 0)
    {
        if (coefficients.Length < 16 || levels.Length < 16)
            throw new ArgumentException("A block has 16 coefficients.");

        ref var input = ref MemoryMarshal.GetReference(coefficients);
        ref var table = ref MemoryMarshal.GetArrayDataReference(_table16);
        ref var bias = ref MemoryMarshal.GetArrayDataReference(_unpackedBias);
        var value = Vector256.LoadUnsafe(ref input);
        var magnitude = Avx2.Abs(value).AsInt16() + Vector256.LoadUnsafe(ref table, Sharpen16);
        var nonZero = Vector256.GreaterThan(magnitude, Vector256.LoadUnsafe(ref table, Threshold16));

        // magnitude x reciprocal in 32 bits: the halves of each product, interleaved within each 128-bit lane
        // by the unpacks, which the pack below undoes.
        var reciprocal = Vector256.LoadUnsafe(ref table, Reciprocal16).AsUInt16();
        var low = Avx2.MultiplyLow(magnitude.AsUInt16(), reciprocal);
        var high = Avx2.MultiplyHigh(magnitude.AsUInt16(), reciprocal);
        var products0 = Avx2.UnpackLow(low, high).AsInt32() + Vector256.LoadUnsafe(ref bias);
        var products1 = Avx2.UnpackHigh(low, high).AsInt32() + Vector256.LoadUnsafe(ref bias, 8);
        var level = Avx2.PackUnsignedSaturate(Avx2.ShiftRightLogical(products0, FixBits), Avx2.ShiftRightLogical(products1, FixBits)).AsInt16();
        level = Avx2.Min(level & nonZero, Vector256.Create((short)MaxLevel));
        level = Avx2.Sign(level, value);
        var rebuilt = Avx2.MultiplyLow(level, Vector256.LoadUnsafe(ref table, Step16));

        // With a Y2 block, position 0 is the DC it carries: left as it is, and not a level.
        if (first > 0)
        {
            var keepDc = Vector256.Create((short)-1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            level = Vector256.AndNot(level, keepDc);
            rebuilt = Vector256.ConditionalSelect(keepDc, value, rebuilt);
        }

        rebuilt.StoreUnsafe(ref input);
        return StoreInScanOrder(level.GetLower(), level.GetUpper(), levels);
    }

    /// <summary>
    ///     The same, a row of four coefficients at a time in raster order; the levels are then moved into scan
    ///     order by two shuffles of eight.
    /// </summary>
    internal bool QuantizeVector(Span<short> coefficients, Span<short> levels, int first = 0)
    {
        if (coefficients.Length < 16 || levels.Length < 16)
            throw new ArgumentException("A block has 16 coefficients.");

        ref var input = ref MemoryMarshal.GetReference(coefficients);
        ref var table = ref MemoryMarshal.GetArrayDataReference(_table);
        var low = Vector128.LoadUnsafe(ref input);
        var high = Vector128.LoadUnsafe(ref input, 8);
        var v0 = Vector128.WidenLower(low);
        var v1 = Vector128.WidenUpper(low);
        var v2 = Vector128.WidenLower(high);
        var v3 = Vector128.WidenUpper(high);

        // With a Y2 block, position 0 is the DC it carries: left as it is, and not a level.
        var keepDc = first > 0 ? Vector128.Create(-1, 0, 0, 0) : Vector128<int>.Zero;
        var (l0, d0) = Row(v0, ref table, 0);
        var (l1, d1) = Row(v1, ref table, 4);
        var (l2, d2) = Row(v2, ref table, 8);
        var (l3, d3) = Row(v3, ref table, 12);
        l0 = Vector128.AndNot(l0, keepDc);
        d0 = Vector128.ConditionalSelect(keepDc, v0, d0);

        Vector128.Narrow(d0, d1).StoreUnsafe(ref input);
        Vector128.Narrow(d2, d3).StoreUnsafe(ref input, 8);
        return StoreInScanOrder(Vector128.Narrow(l0, l1), Vector128.Narrow(l2, l3), levels);
    }

    /// <summary>Writes the 16 levels, given in raster order, to <paramref name="levels" /> in scan order; returns whether any is not zero.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool StoreInScanOrder(Vector128<short> rasterLow, Vector128<short> rasterHigh, Span<short> levels)
    {
        // Scan order: 0 1 4 8 5 2 3 6 | 9 12 13 10 7 11 14 15, an index of 8 or more taking nothing.
        var scanLow = Vector128.Shuffle(rasterLow, Vector128.Create((short)0, 1, 4, 8, 5, 2, 3, 6)) |
                      Vector128.Shuffle(rasterHigh, Vector128.Create((short)8, 8, 8, 0, 8, 8, 8, 8));
        var scanHigh = Vector128.Shuffle(rasterHigh, Vector128.Create((short)1, 4, 5, 2, 8, 3, 6, 7)) |
                       Vector128.Shuffle(rasterLow, Vector128.Create((short)8, 8, 8, 8, 7, 8, 8, 8));
        ref var output = ref MemoryMarshal.GetReference(levels);
        scanLow.StoreUnsafe(ref output);
        scanHigh.StoreUnsafe(ref output, 8);
        return (scanLow | scanHigh) != Vector128<short>.Zero;
    }

    /// <summary>One row of four: the signed levels, and what the decoder rebuilds from them.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<int> Levels, Vector128<int> Rebuilt) Row(Vector128<int> value, ref int table, int at)
    {
        var step = Vector128.LoadUnsafe(ref table, (nuint)(Step + at));
        var reciprocal = Vector128.LoadUnsafe(ref table, (nuint)(Reciprocal + at));
        var bias = Vector128.LoadUnsafe(ref table, (nuint)(Bias + at));
        var threshold = Vector128.LoadUnsafe(ref table, (nuint)(Threshold + at));
        var sharpen = Vector128.LoadUnsafe(ref table, (nuint)(Sharpen + at));
        var negative = Vector128.LessThan(value, Vector128<int>.Zero);
        var magnitude = Vector128.Abs(value) + sharpen;
        var level = Vector128.ShiftRightLogical(magnitude * reciprocal + bias, FixBits);
        level = Vector128.Min(level & Vector128.GreaterThan(magnitude, threshold), Vector128.Create(MaxLevel));
        level = (level ^ negative) - negative;
        return (level, level * step);
    }

    internal bool QuantizeScalar(Span<short> coefficients, Span<short> levels, int first = 0)
    {
        if (coefficients.Length < 16 || levels.Length < 16)
            throw new ArgumentException("A block has 16 coefficients.");

        var zigzag = Vp8Tables.Zigzag;
        ref var input = ref MemoryMarshal.GetReference(coefficients);
        ref var output = ref MemoryMarshal.GetReference(levels);
        ref var table = ref MemoryMarshal.GetArrayDataReference(_table);
        var nonZero = 0;
        for (var n = 0; n < first; n++)
            Unsafe.Add(ref output, n) = 0;
        for (var n = first; n < 16; n++)
        {
            int j = zigzag[n];
            ref var coefficient = ref Unsafe.Add(ref input, j);
            int value = coefficient;
            var magnitude = (value < 0 ? -value : value) + Unsafe.Add(ref table, Sharpen + j);
            if (magnitude > Unsafe.Add(ref table, Threshold + j))
            {
                var level = (int)(((uint)magnitude * (uint)Unsafe.Add(ref table, Reciprocal + j) + (uint)Unsafe.Add(ref table, Bias + j)) >> FixBits);
                if (level > MaxLevel)
                    level = MaxLevel;
                if (value < 0)
                    level = -level;
                coefficient = (short)(level * Unsafe.Add(ref table, Step + j));
                Unsafe.Add(ref output, n) = (short)level;
                nonZero |= level;
            }
            else
            {
                coefficient = 0;
                Unsafe.Add(ref output, n) = 0;
            }
        }

        return nonZero != 0;
    }
}
