using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The YUV 4:2:0 planes a lossy frame is encoded from, each padded to whole macroblocks by repeating its
///     last column and row. RGB comes to YUV with libwebp's integer BT.601 coefficients; each chroma sample
///     averages its four pixels in a gamma-compressed light (gamma 0.8, as libwebp does, so dark and light
///     pixels weigh as they look), weighted by alpha where the four are partly transparent.
/// </summary>
internal sealed class Vp8Picture : IDisposable
{
    private const int GammaFix = 12;
    private const int GammaTableFix = 7;
    private const int GammaTableSize = 1 << (GammaFix - GammaTableFix);

    private static readonly ushort[] GammaToLinear = BuildGammaToLinear();
    private static readonly int[] LinearToGammaTable = BuildLinearToGamma();

    public Vp8Picture(ReadOnlySpan<byte> rgba, int width, int height)
    {
        Width = width;
        Height = height;
        MacroblocksWide = (width + 15) >> 4;
        MacroblocksHigh = (height + 15) >> 4;
        YStride = MacroblocksWide * 16;
        UvStride = MacroblocksWide * 8;
        var pool = ArrayPool<byte>.Shared;
        Y = pool.Rent(YStride * MacroblocksHigh * 16);
        U = pool.Rent(UvStride * MacroblocksHigh * 8);
        V = pool.Rent(UvStride * MacroblocksHigh * 8);
        ConvertLuma(rgba);
        ConvertChroma(rgba);
        Pad(Y, YStride, width, height, MacroblocksHigh * 16);
        var uvWidth = (width + 1) >> 1;
        var uvHeight = (height + 1) >> 1;
        Pad(U, UvStride, uvWidth, uvHeight, MacroblocksHigh * 8);
        Pad(V, UvStride, uvWidth, uvHeight, MacroblocksHigh * 8);
    }

    public int Width { get; }

    public int Height { get; }

    public int MacroblocksWide { get; }

    public int MacroblocksHigh { get; }

    public byte[] Y { get; }

    public byte[] U { get; }

    public byte[] V { get; }

    public int YStride { get; }

    public int UvStride { get; }

    public void Dispose()
    {
        var pool = ArrayPool<byte>.Shared;
        pool.Return(Y);
        pool.Return(U);
        pool.Return(V);
    }

    private void ConvertLuma(ReadOnlySpan<byte> rgba)
    {
        for (var y = 0; y < Height; y++)
        {
            var source = rgba.Slice(y * Width * 4, Width * 4);
            var row = Y.AsSpan(y * YStride, Width);
            var x = Vector128.IsHardwareAccelerated ? ConvertLumaVector(source, row) : 0;
            for (; x < row.Length; x++)
            {
                var p = x * 4;
                row[x] = (byte)((16839 * source[p] + 33059 * source[p + 1] + 6420 * source[p + 2] + (1 << 15) + (16 << 16)) >> 16);
            }
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

    private void ConvertChroma(ReadOnlySpan<byte> rgba)
    {
        var stride = Width * 4;
        for (var y = 0; y < Height; y += 2)
        {
            var top = y * stride;
            var bottom = y + 1 < Height ? top + stride : top;
            var u = U.AsSpan((y >> 1) * UvStride);
            var v = V.AsSpan((y >> 1) * UvStride);
            for (var x = 0; x < Width; x += 2)
            {
                var right = x + 1 < Width ? 4 : 0;
                var a = top + x * 4;
                var b = bottom + x * 4;
                var alpha = rgba[a + 3] + rgba[a + right + 3] + rgba[b + 3] + rgba[b + right + 3];
                int r, g, bl;
                if (alpha is 0 or 4 * 255)
                {
                    r = Average(rgba, a, b, right, 0);
                    g = Average(rgba, a, b, right, 1);
                    bl = Average(rgba, a, b, right, 2);
                }
                else
                {
                    r = WeightedAverage(rgba, a, b, right, 0, alpha);
                    g = WeightedAverage(rgba, a, b, right, 1, alpha);
                    bl = WeightedAverage(rgba, a, b, right, 2, alpha);
                }

                const int rounding = (1 << 15) << 2;
                u[x >> 1] = ClipUv(-9719 * r - 19081 * g + 28800 * bl + rounding);
                v[x >> 1] = ClipUv(28800 * r - 24116 * g - 4684 * bl + rounding);
            }
        }
    }

    /// <summary>Four times the gamma-compressed mean of a channel over the 2 x 2 block.</summary>
    private static int Average(ReadOnlySpan<byte> rgba, int a, int b, int right, int channel) =>
        LinearToGamma(GammaToLinear[rgba[a + channel]] + GammaToLinear[rgba[a + right + channel]] +
                      GammaToLinear[rgba[b + channel]] + GammaToLinear[rgba[b + right + channel]]);

    /// <summary>The same, each pixel weighted by its alpha, so a transparent pixel's colour does not bleed in.</summary>
    private static int WeightedAverage(ReadOnlySpan<byte> rgba, int a, int b, int right, int channel, int alpha)
    {
        var sum = rgba[a + 3] * GammaToLinear[rgba[a + channel]] + rgba[a + right + 3] * GammaToLinear[rgba[a + right + channel]] +
                  rgba[b + 3] * GammaToLinear[rgba[b + channel]] + rgba[b + right + 3] * GammaToLinear[rgba[b + right + channel]];
        return LinearToGamma((int)(4L * sum / alpha));
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

    private static void Pad(byte[] plane, int stride, int width, int height, int paddedHeight)
    {
        for (var y = 0; y < height; y++)
        {
            var row = plane.AsSpan(y * stride, stride);
            row[width..].Fill(row[width - 1]);
        }

        for (var y = height; y < paddedHeight; y++)
            plane.AsSpan((height - 1) * stride, stride).CopyTo(plane.AsSpan(y * stride, stride));
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
}
