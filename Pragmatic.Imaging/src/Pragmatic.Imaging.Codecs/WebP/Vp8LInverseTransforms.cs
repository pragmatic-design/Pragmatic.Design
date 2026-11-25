using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>The four lossless transforms, inverted over ARGB pixels in place (RFC 9649, section 4).</summary>
internal static class Vp8LInverseTransforms
{
    /// <summary>Inverts a predictor, cross-colour or subtract-green transform over <paramref name="pixels" />, <paramref name="height" /> rows.</summary>
    public static void Apply(Vp8LTransform transform, Span<uint> pixels, int height)
    {
        switch (transform.Kind)
        {
            case Vp8LTransform.Predictor:
                Vp8LPredictor.Invert(pixels, transform.Width, height, transform.Bits, transform.Data);
                return;
            case Vp8LTransform.CrossColor:
                CrossColor(pixels, transform.Width, height, transform.Bits, transform.Data);
                return;
            default:
                AddGreen(pixels);
                return;
        }
    }

    private static void CrossColor(Span<uint> pixels, int width, int height, int bits, uint[] elements)
    {
        var blocksPerRow = DivideRoundingUp(width, bits);
        var blockSize = 1 << bits;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var blockRow = (y >> bits) * blocksPerRow;
            for (var start = 0; start < width; start += blockSize)
            {
                var element = elements[blockRow + (start >> bits)];
                var greenToRed = (sbyte)element;
                var greenToBlue = (sbyte)(element >> 8);
                var redToBlue = (sbyte)(element >> 16);
                var end = Math.Min(start + blockSize, width);
                for (var x = start; x < end; x++)
                {
                    var argb = row[x];
                    var green = (sbyte)(argb >> 8);
                    var red = (int)(argb >> 16) & 0xFF;
                    var blue = (int)argb & 0xFF;
                    red = (red + Delta(greenToRed, green)) & 0xFF;
                    blue = (blue + Delta(greenToBlue, green) + Delta(redToBlue, (sbyte)red)) & 0xFF;
                    row[x] = (argb & 0xFF00FF00) | ((uint)red << 16) | (uint)blue;
                }
            }
        }
    }

    private static void AddGreen(Span<uint> pixels)
    {
        var i = 0;
        var byteMask = new Vector<uint>(0xFF);
        var alphaGreen = new Vector<uint>(0xFF00FF00);
        var redBlueMask = new Vector<uint>(0x00FF00FF);
        for (; i + Vector<uint>.Count <= pixels.Length; i += Vector<uint>.Count)
        {
            var argb = new Vector<uint>(pixels[i..]);
            var green = Vector.ShiftRightLogical(argb, 8) & byteMask;
            var added = ((argb & redBlueMask) + (Vector.ShiftLeft(green, 16) | green)) & redBlueMask;
            ((argb & alphaGreen) | added).CopyTo(pixels[i..]);
        }

        for (; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var green = (argb >> 8) & 0xFF;
            var redBlue = (argb & 0x00FF00FF) + ((green << 16) | green);
            pixels[i] = (argb & 0xFF00FF00) | (redBlue & 0x00FF00FF);
        }
    }

    /// <summary>
    ///     Widens <paramref name="packed" /> into <paramref name="output" />, each index its colour; an index
    ///     past the table is transparent black, as the format defines. The two may be the same pixels when
    ///     nothing is packed.
    /// </summary>
    /// <param name="packed">The indices, <c>2^bits</c> to a pixel's green.</param>
    /// <param name="output">The colours, <paramref name="width" /> to a row.</param>
    /// <param name="width">The width of the widened image.</param>
    /// <param name="height">The rows.</param>
    /// <param name="bits">How many indices share a pixel, as a power of two.</param>
    /// <param name="palette">256 colours, those past the table's own zero.</param>
    public static void Index(Span<uint> packed, Span<uint> output, int width, int height, int bits, uint[] palette)
    {
        if (bits == 0)
        {
            for (var i = 0; i < width * height; i++)
                output[i] = palette[(int)(packed[i] >> 8) & 0xFF];
            return;
        }

        var packedWidth = DivideRoundingUp(width, bits);
        var pixelBits = 8 >> bits;
        var mask = (1 << pixelBits) - 1;
        var perPixel = 1 << bits;
        for (var y = 0; y < height; y++)
        {
            var source = packed.Slice(y * packedWidth, packedWidth);
            var row = output.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var green = (int)(source[x >> bits] >> 8) & 0xFF;
                row[x] = palette[(green >> ((x & (perPixel - 1)) * pixelBits)) & mask];
            }
        }
    }

    public static int DivideRoundingUp(int size, int bits) => (size + (1 << bits) - 1) >> bits;

    /// <summary>Per-channel addition modulo 256.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Add(uint a, uint b)
    {
        var alphaGreen = (a & 0xFF00FF00) + (b & 0xFF00FF00);
        var redBlue = (a & 0x00FF00FF) + (b & 0x00FF00FF);
        return (alphaGreen & 0xFF00FF00) | (redBlue & 0x00FF00FF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Delta(sbyte transform, sbyte colour) => (transform * colour) >> 5;
}
