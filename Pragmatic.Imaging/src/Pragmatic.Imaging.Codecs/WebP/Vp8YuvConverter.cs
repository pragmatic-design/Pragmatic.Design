// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     A lossy frame to RGBA the way libwebp writes it by default: chroma brought to full resolution by its
///     "fancy" upsampler (each output sample weighs the four nearest chroma samples 9:3:3:1), then BT.601
///     limited-range YUV to RGB in libwebp's 14-bit fixed point (<c>src/dsp/yuv.h</c>). Alpha is left at 255.
/// </summary>
internal static class Vp8YuvConverter
{
    public static void ToRgba(Vp8Frame frame, Span<byte> rgba)
    {
        var width = frame.Width;
        var height = frame.Height;
        var stride = width * 4;

        // The first row has no chroma row above, so it uses its own twice; so does the last row of an
        // even height. Rows in between come in pairs that straddle a chroma row boundary.
        Row(frame, 0, -1, 0, 0, rgba, stride);
        var y = 1;
        for (; y + 1 < height; y += 2)
            Row(frame, y, y + 1, (y - 1) >> 1, (y + 1) >> 1, rgba, stride);
        if ((height & 1) == 0)
            Row(frame, height - 1, -1, (height >> 1) - 1, (height >> 1) - 1, rgba, stride);
    }

    /// <summary>
    ///     libwebp's <c>UpsampleRgbaLinePair</c>: the luma row <paramref name="top" /> sits nearer chroma row
    ///     <paramref name="topUv" />, the row <paramref name="bottom" /> (or none, -1) nearer <paramref name="uv" />.
    /// </summary>
    private static void Row(Vp8Frame frame, int top, int bottom, int topUv, int uv, Span<byte> rgba, int stride)
    {
        var width = frame.Width;
        var yRow = frame.Y.AsSpan(top * frame.YStride, width);
        var bottomRow = bottom >= 0 ? frame.Y.AsSpan(bottom * frame.YStride, width) : default;
        var topU = frame.U.AsSpan(topUv * frame.UvStride);
        var topV = frame.V.AsSpan(topUv * frame.UvStride);
        var curU = frame.U.AsSpan(uv * frame.UvStride);
        var curV = frame.V.AsSpan(uv * frame.UvStride);
        var topOut = rgba.Slice(top * stride, stride);
        var bottomOut = bottom >= 0 ? rgba.Slice(bottom * stride, stride) : default;

        var lastPair = (width - 1) >> 1;
        var tl = Load(topU[0], topV[0]);
        var l = Load(curU[0], curV[0]);
        {
            var uv0 = (3 * tl + l + 0x00020002u) >> 2;
            Pixel(yRow[0], uv0, topOut, 0);
        }

        if (bottom >= 0)
        {
            var uv0 = (3 * l + tl + 0x00020002u) >> 2;
            Pixel(bottomRow[0], uv0, bottomOut, 0);
        }

        for (var x = 1; x <= lastPair; x++)
        {
            var t = Load(topU[x], topV[x]);
            var c = Load(curU[x], curV[x]);
            var average = tl + t + l + c + 0x00080008u;
            var diagonal12 = (average + 2 * (t + l)) >> 3;
            var diagonal03 = (average + 2 * (tl + c)) >> 3;
            Pixel(yRow[2 * x - 1], (diagonal12 + tl) >> 1, topOut, 2 * x - 1);
            Pixel(yRow[2 * x], (diagonal03 + t) >> 1, topOut, 2 * x);
            if (bottom >= 0)
            {
                Pixel(bottomRow[2 * x - 1], (diagonal03 + l) >> 1, bottomOut, 2 * x - 1);
                Pixel(bottomRow[2 * x], (diagonal12 + c) >> 1, bottomOut, 2 * x);
            }

            tl = t;
            l = c;
        }

        if ((width & 1) == 0)
        {
            var uv0 = (3 * tl + l + 0x00020002u) >> 2;
            Pixel(yRow[width - 1], uv0, topOut, width - 1);
            if (bottom >= 0)
            {
                uv0 = (3 * l + tl + 0x00020002u) >> 2;
                Pixel(bottomRow[width - 1], uv0, bottomOut, width - 1);
            }
        }
    }

    /// <summary>U in the low 16 bits and V in the high, so one addition carries both.</summary>
    private static uint Load(byte u, byte v) => u | ((uint)v << 16);

    private static void Pixel(int y, uint uv, Span<byte> row, int x)
    {
        var u = (int)(uv & 0xFF);
        var v = (int)((uv >> 16) & 0xFF);
        var luma = MultiplyHigh(y, 19077);
        var at = x * 4;
        row[at] = Clip(luma + MultiplyHigh(v, 26149) - 14234);
        row[at + 1] = Clip(luma - MultiplyHigh(u, 6419) - MultiplyHigh(v, 13320) + 8708);
        row[at + 2] = Clip(luma + MultiplyHigh(u, 33050) - 17685);
        row[at + 3] = 255;
    }

    private static int MultiplyHigh(int value, int coefficient) => (value * coefficient) >> 8;

    /// <summary>From the 6-bit fixed point back to a byte, clamped.</summary>
    private static byte Clip(int value) => (value & ~16383) == 0 ? (byte)(value >> 6) : value < 0 ? (byte)0 : (byte)255;
}
