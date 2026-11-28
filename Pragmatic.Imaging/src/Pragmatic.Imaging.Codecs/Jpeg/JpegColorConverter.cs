using System.Buffers;

namespace Pragmatic.Imaging.Codecs.Jpeg;

/// <summary>
///     From component planes to RGBA: chroma brought up to the luma grid (the triangle filter of the
///     IJG's "fancy upsampling" for the common 2× cases, replication otherwise), then JFIF's YCbCr to RGB in
///     fixed point. Works a row at a time with three pooled rows of scratch. At a reduced decode scale
///     the planes are smaller and the geometry the same.
/// </summary>
internal static class JpegColorConverter
{
    public static void ToRgba(JpegFrame frame, PixelBuffer output)
    {
        if (frame.Components.Length == 1)
        {
            GrayToRgba(frame, output);
            return;
        }

        var y = frame.Components[0];
        var cb = frame.Components[1];
        var cr = frame.Components[2];
        if (y.HorizontalFactor != frame.MaxHorizontalFactor || y.VerticalFactor != frame.MaxVerticalFactor)
            throw new ImageDecodeException("JPEG luma is subsampled, which is not supported.");

        var width = frame.OutputWidth;
        var pool = ArrayPool<byte>.Shared;
        var cbRow = pool.Rent(width);
        var crRow = pool.Rent(width);
        var blend = pool.Rent(Math.Max(cb.ScaledWidth, cr.ScaledWidth));
        try
        {
            for (var row = 0; row < frame.OutputHeight; row++)
            {
                UpsampledRow(cb, frame, row, cbRow.AsSpan(0, width), blend);
                UpsampledRow(cr, frame, row, crRow.AsSpan(0, width), blend);
                var luma = y.Plane.AsSpan(row * y.PlaneStride, width);
                YCbCrToRgba(luma, cbRow, crRow, output.Row(row));
            }
        }
        finally
        {
            pool.Return(cbRow);
            pool.Return(crRow);
            pool.Return(blend);
        }
    }

    /// <summary>The components interleaved, unconverted: luma alone, or Y, Cb and Cr with the chroma upsampled as for RGB.</summary>
    public static void ToComponents(JpegFrame frame, Span<byte> output)
    {
        var width = frame.OutputWidth;
        var y = frame.Components[0];
        if (frame.Components.Length == 1)
        {
            for (var row = 0; row < frame.OutputHeight; row++)
                y.Plane.AsSpan(row * y.PlaneStride, width).CopyTo(output.Slice(row * width, width));
            return;
        }

        var cb = frame.Components[1];
        var cr = frame.Components[2];
        if (y.HorizontalFactor != frame.MaxHorizontalFactor || y.VerticalFactor != frame.MaxVerticalFactor)
            throw new ImageDecodeException("JPEG luma is subsampled, which is not supported.");

        var pool = ArrayPool<byte>.Shared;
        var cbRow = pool.Rent(width);
        var crRow = pool.Rent(width);
        var blend = pool.Rent(Math.Max(cb.ScaledWidth, cr.ScaledWidth));
        try
        {
            for (var row = 0; row < frame.OutputHeight; row++)
            {
                UpsampledRow(cb, frame, row, cbRow.AsSpan(0, width), blend);
                UpsampledRow(cr, frame, row, crRow.AsSpan(0, width), blend);
                var luma = y.Plane.AsSpan(row * y.PlaneStride, width);
                var target = output.Slice(row * width * 3, width * 3);
                for (int x = 0, o = 0; x < width; x++, o += 3)
                {
                    target[o] = luma[x];
                    target[o + 1] = cbRow[x];
                    target[o + 2] = crRow[x];
                }
            }
        }
        finally
        {
            pool.Return(cbRow);
            pool.Return(crRow);
            pool.Return(blend);
        }
    }

    private static void GrayToRgba(JpegFrame frame, PixelBuffer output)
    {
        var c = frame.Components[0];
        var width = frame.OutputWidth;
        for (var row = 0; row < frame.OutputHeight; row++)
        {
            var luma = c.Plane.AsSpan(row * c.PlaneStride, width);
            var rgba = output.Row(row);
            for (int x = 0, o = 0; x < width; x++, o += 4)
            {
                var v = luma[x];
                rgba[o] = v;
                rgba[o + 1] = v;
                rgba[o + 2] = v;
                rgba[o + 3] = 255;
            }
        }
    }

    /// <summary>One chroma component's samples along output row <paramref name="row" />, at full width.</summary>
    private static void UpsampledRow(JpegComponent c, JpegFrame frame, int row, Span<byte> output, byte[] blend)
    {
        var hScale = frame.MaxHorizontalFactor / c.HorizontalFactor;
        var vScale = frame.MaxVerticalFactor / c.VerticalFactor;
        var width = frame.OutputWidth;
        var chromaWidth = c.ScaledWidth;
        var last = chromaWidth - 1;

        if (hScale == 1 && vScale == 1)
        {
            c.Plane.AsSpan(row * c.PlaneStride, width).CopyTo(output);
            return;
        }

        // Vertically: the two nearest chroma rows blended 3:1 (2×), or the one row that covers (other).
        ReadOnlySpan<byte> source;
        var lastRow = c.ScaledHeight - 1;
        if (vScale == 2)
        {
            var near = Math.Min(lastRow, row / 2);
            var far = (row & 1) == 0 ? Math.Max(0, near - 1) : Math.Min(lastRow, near + 1);
            var nearRow = c.Plane.AsSpan(near * c.PlaneStride, chromaWidth);
            var farRow = c.Plane.AsSpan(far * c.PlaneStride, chromaWidth);
            var blended = blend.AsSpan(0, chromaWidth);
            BlendThreeToOne(nearRow, farRow, blended);
            source = blended;
        }
        else
        {
            source = c.Plane.AsSpan(Math.Min(lastRow, row / vScale) * c.PlaneStride, chromaWidth);
        }

        if (hScale == 2)
        {
            // Horizontally 2×: each output sample is 3/4 its own chroma sample and 1/4 the neighbour's.
            var fullPairs = width / 2;
            if (fullPairs > 0)
            {
                output[0] = (byte)((3 * source[0] + source[0] + 2) >> 2);
                output[1] = (byte)((3 * source[0] + source[Math.Min(last, 1)] + 1) >> 2);
            }

            var i = JpegColorConverterAvx2.IsSupported ? JpegColorConverterAvx2.UpsampleHorizontal2x(source, output, fullPairs) : 1;
            for (; i < fullPairs; i++)
            {
                var s = source[i];
                var x = i * 2;
                output[x] = (byte)((3 * s + source[i - 1] + 2) >> 2);
                output[x + 1] = (byte)((3 * s + source[Math.Min(last, i + 1)] + 1) >> 2);
            }

            if ((width & 1) == 1)
            {
                var tail = Math.Min(last, fullPairs);
                output[width - 1] = (byte)((3 * source[tail] + source[Math.Max(0, tail - 1)] + 2) >> 2);
            }

            return;
        }

        for (var x = 0; x < width; x++)
            output[x] = source[Math.Min(last, x / hScale)];
    }

    /// <summary>JFIF: R = Y + 1.402 Cr', G = Y − 0.344136 Cb' − 0.714136 Cr', B = Y + 1.772 Cb', with Cb' = Cb − 128.</summary>
    private static void YCbCrToRgba(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgba)
    {
        const int shift = 16;
        const int half = 1 << (shift - 1);
        const int crToR = 91881; // 1.402 * 65536
        const int cbToG = 22554; // 0.344136
        const int crToG = 46802; // 0.714136
        const int cbToB = 116130; // 1.772

        var done = JpegColorConverterAvx2.IsSupported ? JpegColorConverterAvx2.YCbCrToRgba(y, cb, cr, rgba) : 0;
        for (int x = done, o = done * 4; x < y.Length; x++, o += 4)
        {
            var luma = y[x] << shift;
            var cbv = cb[x] - 128;
            var crv = cr[x] - 128;
            rgba[o] = Clamp((luma + crToR * crv + half) >> shift);
            rgba[o + 1] = Clamp((luma - cbToG * cbv - crToG * crv + half) >> shift);
            rgba[o + 2] = Clamp((luma + cbToB * cbv + half) >> shift);
            rgba[o + 3] = 255;
        }
    }

    /// <summary>out[i] = (3·near[i] + far[i] + 2) &gt;&gt; 2, a vector of 16-bit lanes at a time.</summary>
    private static void BlendThreeToOne(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output)
    {
        var i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && near.Length >= System.Numerics.Vector<byte>.Count)
        {
            var nears = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, System.Numerics.Vector<byte>>(near);
            var fars = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, System.Numerics.Vector<byte>>(far);
            var outs = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, System.Numerics.Vector<byte>>(output);
            var three = new System.Numerics.Vector<ushort>(3);
            var two = new System.Numerics.Vector<ushort>(2);
            for (var v = 0; v < nears.Length; v++)
            {
                System.Numerics.Vector.Widen(nears[v], out var nearLow, out var nearHigh);
                System.Numerics.Vector.Widen(fars[v], out var farLow, out var farHigh);
                var low = System.Numerics.Vector.ShiftRightLogical(nearLow * three + farLow + two, 2);
                var high = System.Numerics.Vector.ShiftRightLogical(nearHigh * three + farHigh + two, 2);
                outs[v] = System.Numerics.Vector.Narrow(low, high);
            }

            i = nears.Length * System.Numerics.Vector<byte>.Count;
        }

        for (; i < near.Length; i++)
            output[i] = (byte)((3 * near[i] + far[i] + 2) >> 2);
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
