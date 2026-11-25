using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The four lossless transforms, inverted over ARGB pixels (RFC 9649, section 4).</summary>
internal static class Vp8LInverseTransforms
{
    /// <summary>
    ///     Inverts <paramref name="transform" /> over <paramref name="pixels" />, <paramref name="height" /> rows,
    ///     and returns the result: the same array, except for colour indexing with packed pixels, which widens it.
    /// </summary>
    public static uint[] Apply(Vp8LTransform transform, uint[] pixels, int height)
    {
        switch (transform.Kind)
        {
            case Vp8LTransform.Predictor:
                Predict(pixels, transform.Width, height, transform.Bits, transform.Data);
                return pixels;
            case Vp8LTransform.CrossColor:
                CrossColor(pixels, transform.Width, height, transform.Bits, transform.Data);
                return pixels;
            case Vp8LTransform.SubtractGreen:
                AddGreen(pixels, transform.Width * height);
                return pixels;
            default:
                return Index(pixels, transform.Width, height, transform.Bits, transform.Data);
        }
    }

    private static void Predict(uint[] pixels, int width, int height, int bits, uint[] modes)
    {
        var blocksPerRow = DivideRoundingUp(width, bits);

        // The top-left pixel is predicted by opaque black, the rest of the first row by its left
        // neighbour, and the first pixel of every other row by the pixel above it.
        pixels[0] = Add(pixels[0], 0xFF000000);
        for (var x = 1; x < width; x++)
            pixels[x] = Add(pixels[x], pixels[x - 1]);

        for (var y = 1; y < height; y++)
        {
            var row = y * width;
            pixels[row] = Add(pixels[row], pixels[row - width]);
            var blockRow = (y >> bits) * blocksPerRow;
            for (var x = 1; x < width; x++)
            {
                var mode = (int)(modes[blockRow + (x >> bits)] >> 8) & 0xF;
                var i = row + x;
                pixels[i] = Add(pixels[i], Prediction(mode, pixels, i, width));
            }
        }
    }

    /// <summary>The prediction of mode <paramref name="mode" /> for pixel <paramref name="i" />, which is not on the first row or column.</summary>
    /// <remarks>
    ///     The top-right pixel of the last column is, by memory position, the first pixel of the current row:
    ///     the format defines it that way.
    /// </remarks>
    private static uint Prediction(int mode, uint[] pixels, int i, int width)
    {
        var left = pixels[i - 1];
        var top = pixels[i - width];
        return mode switch
        {
            1 => left,
            2 => top,
            3 => pixels[i - width + 1],
            4 => pixels[i - width - 1],
            5 => Average(Average(left, pixels[i - width + 1]), top),
            6 => Average(left, pixels[i - width - 1]),
            7 => Average(left, top),
            8 => Average(pixels[i - width - 1], top),
            9 => Average(top, pixels[i - width + 1]),
            10 => Average(Average(left, pixels[i - width - 1]), Average(top, pixels[i - width + 1])),
            11 => Select(left, top, pixels[i - width - 1]),
            12 => ClampAddSubtractFull(left, top, pixels[i - width - 1]),
            13 => ClampAddSubtractHalf(Average(left, top), pixels[i - width - 1]),
            _ => 0xFF000000,
        };
    }

    private static void CrossColor(uint[] pixels, int width, int height, int bits, uint[] elements)
    {
        var blocksPerRow = DivideRoundingUp(width, bits);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var blockRow = (y >> bits) * blocksPerRow;
            for (var x = 0; x < width; x++)
            {
                var element = elements[blockRow + (x >> bits)];
                var greenToRed = (sbyte)element;
                var greenToBlue = (sbyte)(element >> 8);
                var redToBlue = (sbyte)(element >> 16);
                var argb = pixels[row + x];
                var green = (sbyte)(argb >> 8);
                var red = (int)(argb >> 16) & 0xFF;
                var blue = (int)argb & 0xFF;
                red = (red + Delta(greenToRed, green)) & 0xFF;
                blue = (blue + Delta(greenToBlue, green)) & 0xFF;
                blue = (blue + Delta(redToBlue, (sbyte)red)) & 0xFF;
                pixels[row + x] = (argb & 0xFF00FF00) | ((uint)red << 16) | (uint)blue;
            }
        }
    }

    private static void AddGreen(uint[] pixels, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var argb = pixels[i];
            var green = (argb >> 8) & 0xFF;
            var redBlue = (argb & 0x00FF00FF) + ((green << 16) | green);
            pixels[i] = (argb & 0xFF00FF00) | (redBlue & 0x00FF00FF);
        }
    }

    /// <summary>The colour of each index; an index past the table is transparent black, as the format defines.</summary>
    private static uint[] Index(uint[] packed, int width, int height, int bits, uint[] palette)
    {
        var colours = new uint[256];
        palette.AsSpan(0, Math.Min(palette.Length, 256)).CopyTo(colours);
        var output = bits == 0 ? packed : new uint[width * height];
        var packedWidth = DivideRoundingUp(width, bits);
        var pixelBits = 8 >> bits;
        var mask = (1 << pixelBits) - 1;
        var perByte = 1 << bits;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var green = (int)(packed[y * packedWidth + (x >> bits)] >> 8) & 0xFF;
                var index = (green >> ((x & (perByte - 1)) * pixelBits)) & mask;
                output[y * width + x] = colours[index];
            }
        }

        return output;
    }

    public static int DivideRoundingUp(int size, int bits) => (size + (1 << bits) - 1) >> bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Delta(sbyte transform, sbyte colour) => (transform * colour) >> 5;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Add(uint a, uint b)
    {
        var alphaGreen = (a & 0xFF00FF00) + (b & 0xFF00FF00);
        var redBlue = (a & 0x00FF00FF) + (b & 0x00FF00FF);
        return (alphaGreen & 0xFF00FF00) | (redBlue & 0x00FF00FF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average(uint a, uint b) => (((a ^ b) & 0xFEFEFEFE) >> 1) + (a & b);

    private static uint Select(uint left, uint top, uint topLeft)
    {
        // The distance of the gradient estimate L + T - TL to the left pixel is that of T to TL, and to
        // the top pixel that of L to TL.
        var toLeft = Distance(top, topLeft);
        var toTop = Distance(left, topLeft);
        return toLeft < toTop ? left : top;
    }

    private static int Distance(uint a, uint b)
    {
        var sum = 0;
        for (var shift = 0; shift < 32; shift += 8)
            sum += Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF));
        return sum;
    }

    private static uint ClampAddSubtractFull(uint a, uint b, uint c)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var value = (int)((a >> shift) & 0xFF) + (int)((b >> shift) & 0xFF) - (int)((c >> shift) & 0xFF);
            result |= (uint)Math.Clamp(value, 0, 255) << shift;
        }

        return result;
    }

    private static uint ClampAddSubtractHalf(uint a, uint b)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var ca = (int)((a >> shift) & 0xFF);
            var cb = (int)((b >> shift) & 0xFF);
            result |= (uint)Math.Clamp(ca + (ca - cb) / 2, 0, 255) << shift;
        }

        return result;
    }
}
