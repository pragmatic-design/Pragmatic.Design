using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The YUV 4:2:0 planes a lossy frame is encoded from, each padded to whole macroblocks by repeating its
///     last column and row. RGB comes to YUV with libwebp's integer BT.601 coefficients; each chroma sample
///     averages its four pixels in a gamma-compressed light (gamma 0.8, as libwebp does, so dark and light
///     pixels weigh as they look), weighted by alpha where the four are partly transparent.
/// </summary>
internal sealed class Vp8Picture
{
    /// <summary>The band row of a macroblock row's first line; row 0 is the last line of the row above.</summary>
    public const int FirstRow = 1;

    private const int LumaBandRows = FirstRow + 16;
    private const int ChromaBandRows = FirstRow + 8;
    private const int GammaFix = 12;
    private const int GammaTableFix = 7;
    private const int GammaTableSize = 1 << (GammaFix - GammaTableFix);

    private static readonly ushort[] GammaToLinear = BuildGammaToLinear();
    private static readonly int[] LinearToGammaTable = BuildLinearToGamma();

    // LinearToGamma of every sum of four linear values, 0 to 4 x 4095: the interpolation done once, 32 KB.
    private static readonly ushort[] LinearSumToGamma = BuildLinearSumToGamma();

    /// <summary>Starts a picture of <paramref name="width" /> by <paramref name="height" />: bands from the pool, nothing converted yet.</summary>
    public void Begin(int width, int height)
    {
        Release();
        Width = width;
        Height = height;
        MacroblocksWide = (width + 15) >> 4;
        MacroblocksHigh = (height + 15) >> 4;
        YStride = MacroblocksWide * 16;
        UvStride = MacroblocksWide * 8;
        var pool = ArrayPool<byte>.Shared;
        Y = pool.Rent(YStride * LumaBandRows);
        U = pool.Rent(UvStride * ChromaBandRows);
        V = pool.Rent(UvStride * ChromaBandRows);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int MacroblocksWide { get; private set; }

    public int MacroblocksHigh { get; private set; }

    /// <summary>The luma of one macroblock row, from <see cref="FirstRow" />, after the last line of the row above.</summary>
    public byte[] Y { get; private set; } = [];

    /// <summary>The U of one macroblock row, laid out as <see cref="Y" />.</summary>
    public byte[] U { get; private set; } = [];

    /// <summary>The V of one macroblock row, laid out as <see cref="Y" />.</summary>
    public byte[] V { get; private set; } = [];

    public int YStride { get; private set; }

    public int UvStride { get; private set; }

    /// <summary>Gives the bands back to the pool.</summary>
    public void Release()
    {
        var pool = ArrayPool<byte>.Shared;
        if (Y.Length > 0)
            pool.Return(Y);
        if (U.Length > 0)
            pool.Return(U);
        if (V.Length > 0)
            pool.Return(V);
        Y = U = V = [];
    }

    /// <summary>
    ///     Converts macroblock row <paramref name="mbY" /> of <paramref name="rgba" /> into the bands, padded to
    ///     whole macroblocks; the last line of the row converted before becomes the line above. Rows are
    ///     converted in order from 0, or from 0 again.
    /// </summary>
    public void ConvertRow(ReadOnlySpan<byte> rgba, int mbY)
    {
        if (mbY > 0)
        {
            Y.AsSpan((LumaBandRows - 1) * YStride, YStride).CopyTo(Y.AsSpan(0, YStride));
            U.AsSpan((ChromaBandRows - 1) * UvStride, UvStride).CopyTo(U.AsSpan(0, UvStride));
            V.AsSpan((ChromaBandRows - 1) * UvStride, UvStride).CopyTo(V.AsSpan(0, UvStride));
        }

        var width = Width;
        for (var j = 0; j < 16; j++)
        {
            var y = mbY * 16 + j;
            var row = Y.AsSpan((FirstRow + j) * YStride, YStride);
            if (y >= Height)
            {
                // Below the picture: the last row again, already in this band.
                Y.AsSpan((FirstRow + Height - 1 - mbY * 16) * YStride, YStride).CopyTo(row);
                continue;
            }

            ConvertLuma(rgba.Slice(y * width * 4, width * 4), row[..width]);
            row[width..].Fill(row[width - 1]);
        }

        var uvWidth = (width + 1) >> 1;
        var uvHeight = (Height + 1) >> 1;
        for (var j = 0; j < 8; j++)
        {
            var cy = mbY * 8 + j;
            var u = U.AsSpan((FirstRow + j) * UvStride, UvStride);
            var v = V.AsSpan((FirstRow + j) * UvStride, UvStride);
            if (cy >= uvHeight)
            {
                var last = (FirstRow + uvHeight - 1 - mbY * 8) * UvStride;
                U.AsSpan(last, UvStride).CopyTo(u);
                V.AsSpan(last, UvStride).CopyTo(v);
                continue;
            }

            ConvertChroma(rgba, 2 * cy, u, v);
            u[uvWidth..].Fill(u[uvWidth - 1]);
            v[uvWidth..].Fill(v[uvWidth - 1]);
        }
    }

    private static void ConvertLuma(ReadOnlySpan<byte> source, Span<byte> row)
    {
        var x = Vector128.IsHardwareAccelerated ? ConvertLumaVector(source, row) : 0;
        for (; x < row.Length; x++)
        {
            var p = x * 4;
            row[x] = (byte)((16839 * source[p] + 33059 * source[p + 1] + 6420 * source[p + 2] + (1 << 15) + (16 << 16)) >> 16);
        }
    }

    /// <summary>Sixteen pixels of a row at a time, four to a vector of 32-bit lanes; returns how many it did.</summary>
    private static int ConvertLumaVector(ReadOnlySpan<byte> source, Span<byte> row)
    {
        ref var pixels = ref MemoryMarshal.GetReference(source);
        ref var target = ref MemoryMarshal.GetReference(row);
        var x = 0;
        for (; x + 16 <= row.Length; x += 16)
        {
            var a = Luma(Vector128.LoadUnsafe(ref pixels, (nuint)(x * 4)).AsUInt32());
            var b = Luma(Vector128.LoadUnsafe(ref pixels, (nuint)(x * 4 + 16)).AsUInt32());
            var c = Luma(Vector128.LoadUnsafe(ref pixels, (nuint)(x * 4 + 32)).AsUInt32());
            var d = Luma(Vector128.LoadUnsafe(ref pixels, (nuint)(x * 4 + 48)).AsUInt32());
            Vector128.Narrow(Vector128.Narrow(a, b), Vector128.Narrow(c, d)).StoreUnsafe(ref target, (nuint)x);
        }

        return x;
    }

    private static Vector128<uint> Luma(Vector128<uint> rgba)
    {
        var mask = Vector128.Create(0xFFu);
        var r = rgba & mask;
        var g = Vector128.ShiftRightLogical(rgba, 8) & mask;
        var b = Vector128.ShiftRightLogical(rgba, 16) & mask;
        return Vector128.ShiftRightLogical(r * 16839 + g * 33059 + b * 6420 + Vector128.Create((1u << 15) + (16u << 16)), 16);
    }

    /// <summary>The chroma of image rows <paramref name="y" /> and the one below (or itself, at the bottom), into <paramref name="u" /> and <paramref name="v" />.</summary>
    private void ConvertChroma(ReadOnlySpan<byte> rgba, int y, Span<byte> u, Span<byte> v)
    {
        var width = Width;
        var stride = width * 4;
        var top = y * stride;
        var bottom = y + 1 < Height ? top + stride : top;
        var uvWidth = (width + 1) >> 1;
        if (rgba.Length < Math.Max(top, bottom) + stride || u.Length < uvWidth || v.Length < uvWidth)
            throw new ArgumentException("The rows reach past their buffers.");

        ref var pixels = ref MemoryMarshal.GetReference(rgba);
        ref var uRow = ref MemoryMarshal.GetReference(u);
        ref var vRow = ref MemoryMarshal.GetReference(v);
        for (var i = 0; i < uvWidth; i++)
        {
            // The block's four pixels; at an odd width the last column's right pixel is its left one again.
            var right = 2 * i + 1 < width ? 4 : 0;
            ref var a = ref Unsafe.Add(ref pixels, top + i * 8);
            ref var b = ref Unsafe.Add(ref pixels, bottom + i * 8);
            ref var ar = ref Unsafe.Add(ref a, right);
            ref var br = ref Unsafe.Add(ref b, right);
            var alpha = Unsafe.Add(ref a, 3) + Unsafe.Add(ref ar, 3) + Unsafe.Add(ref b, 3) + Unsafe.Add(ref br, 3);
            int r, g, bl;
            if (alpha is 0 or 4 * 255)
            {
                r = Average(ref a, ref ar, ref b, ref br, 0);
                g = Average(ref a, ref ar, ref b, ref br, 1);
                bl = Average(ref a, ref ar, ref b, ref br, 2);
            }
            else
            {
                r = WeightedAverage(ref a, ref ar, ref b, ref br, 0, alpha);
                g = WeightedAverage(ref a, ref ar, ref b, ref br, 1, alpha);
                bl = WeightedAverage(ref a, ref ar, ref b, ref br, 2, alpha);
            }

            const int rounding = (1 << 15) << 2;
            Unsafe.Add(ref uRow, i) = ClipUv(-9719 * r - 19081 * g + 28800 * bl + rounding);
            Unsafe.Add(ref vRow, i) = ClipUv(28800 * r - 24116 * g - 4684 * bl + rounding);
        }
    }

    /// <summary>Four times the gamma-compressed mean of a channel over the 2 x 2 block.</summary>
    private static int Average(ref byte a, ref byte ar, ref byte b, ref byte br, int channel)
    {
        ref var linear = ref MemoryMarshal.GetArrayDataReference(GammaToLinear);
        var sum = Unsafe.Add(ref linear, Unsafe.Add(ref a, channel)) + Unsafe.Add(ref linear, Unsafe.Add(ref ar, channel)) +
                  Unsafe.Add(ref linear, Unsafe.Add(ref b, channel)) + Unsafe.Add(ref linear, Unsafe.Add(ref br, channel));
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(LinearSumToGamma), sum);
    }

    /// <summary>The same, each pixel weighted by its alpha, so a transparent pixel's colour does not bleed in.</summary>
    private static int WeightedAverage(ref byte a, ref byte ar, ref byte b, ref byte br, int channel, int alpha)
    {
        var linear = GammaToLinear;
        var sum = Unsafe.Add(ref a, 3) * linear[Unsafe.Add(ref a, channel)] + Unsafe.Add(ref ar, 3) * linear[Unsafe.Add(ref ar, channel)] +
                  Unsafe.Add(ref b, 3) * linear[Unsafe.Add(ref b, channel)] + Unsafe.Add(ref br, 3) * linear[Unsafe.Add(ref br, channel)];
        return LinearSumToGamma[(int)(4L * sum / alpha)];
    }

    /// <summary>A sum of four linear values back to four times its gamma-compressed mean, interpolating the table.</summary>
    private static int LinearToGamma(int value)
    {
        var position = value >> (GammaTableFix + 2);
        var fraction = value & ((1 << (GammaTableFix + 2)) - 1);
        var v0 = LinearToGammaTable[position];
        var v1 = LinearToGammaTable[position + 1];
        var y = v1 * fraction + v0 * ((1 << (GammaTableFix + 2)) - fraction);
        return (y + (1 << (GammaTableFix - 1))) >> GammaTableFix;
    }

    private static byte ClipUv(int value)
    {
        value = (value + (128 << 18)) >> 18;
        return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
    }

    private static ushort[] BuildGammaToLinear()
    {
        var table = new ushort[256];
        for (var v = 0; v < 256; v++)
            table[v] = (ushort)(Math.Pow(v / 255.0, 0.8) * ((1 << GammaFix) - 1) + 0.5);
        return table;
    }

    private static int[] BuildLinearToGamma()
    {
        var table = new int[GammaTableSize + 2];
        var scale = (double)(1 << GammaTableFix) / ((1 << GammaFix) - 1);
        for (var v = 0; v <= GammaTableSize; v++)
            table[v] = (int)(255.0 * Math.Pow(scale * v, 1 / 0.8) + 0.5);
        table[GammaTableSize + 1] = table[GammaTableSize];
        return table;
    }

    private static ushort[] BuildLinearSumToGamma()
    {
        var table = new ushort[4 * ((1 << GammaFix) - 1) + 1];
        for (var sum = 0; sum < table.Length; sum++)
            table[sum] = (ushort)LinearToGamma(sum);
        return table;
    }
}
