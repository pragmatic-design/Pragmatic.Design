using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Transforms;

/// <summary>
///     A row of any <see cref="PixelFormat" /> as four floats a pixel, red, green, blue, alpha, in the format's own scale
///     (0 to 255, 0 to 65,535, 0 to 1): what the resampler and the blur work on, linear, so the scale does not change
///     their arithmetic. Grey widens into all three colours, a missing alpha is the scale's top; narrowing rounds half
///     up, clamps to the scale, and keeps what the format holds.
/// </summary>
internal static class PixelRows
{
    /// <summary>The top of a channel: 255, 65,535, or 1 for floats.</summary>
    public static float Scale(PixelFormat format) => format.BitsPerChannel() switch
    {
        8 => 255f,
        16 => 65535f,
        _ => 1f,
    };

    public static void Widen(PixelFormat format, ReadOnlySpan<byte> row, Span<float> floats)
    {
        var width = row.Length / format.BytesPerPixel();
        switch (format)
        {
            case PixelFormat.Rgba8:
                WidenRgba8(row, floats);
                return;
            case PixelFormat.RgbaFloat:
                MemoryMarshal.Cast<byte, float>(row).CopyTo(floats);
                return;
            case PixelFormat.Rgba16:
                for (var i = 0; i < width * 4; i++)
                    floats[i] = BinaryPrimitives.ReadUInt16LittleEndian(row[(i * 2)..]);
                return;
            case PixelFormat.Rgb8:
                for (var x = 0; x < width; x++)
                {
                    floats[x * 4] = row[x * 3];
                    floats[x * 4 + 1] = row[x * 3 + 1];
                    floats[x * 4 + 2] = row[x * 3 + 2];
                    floats[x * 4 + 3] = 255f;
                }

                return;
            case PixelFormat.Rgb16:
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < 3; c++)
                        floats[x * 4 + c] = BinaryPrimitives.ReadUInt16LittleEndian(row[((x * 3 + c) * 2)..]);
                    floats[x * 4 + 3] = 65535f;
                }

                return;
            case PixelFormat.Gray8:
                for (var x = 0; x < width; x++)
                {
                    floats[x * 4] = floats[x * 4 + 1] = floats[x * 4 + 2] = row[x];
                    floats[x * 4 + 3] = 255f;
                }

                return;
            default:
                for (var x = 0; x < width; x++)
                {
                    floats[x * 4] = floats[x * 4 + 1] = floats[x * 4 + 2] = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                    floats[x * 4 + 3] = 65535f;
                }

                return;
        }
    }

    /// <summary>Back to the format: rounded half up and clamped; grey takes the red channel, a format without alpha drops it.</summary>
    public static void Narrow(PixelFormat format, ReadOnlySpan<float> floats, Span<byte> row)
    {
        var width = row.Length / format.BytesPerPixel();
        switch (format)
        {
            case PixelFormat.Rgba8:
                NarrowRgba8(floats, row);
                return;
            case PixelFormat.RgbaFloat:
                floats[..(width * 4)].CopyTo(MemoryMarshal.Cast<byte, float>(row));
                return;
            case PixelFormat.Rgba16:
                for (var i = 0; i < width * 4; i++)
                    BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], To16(floats[i]));
                return;
            case PixelFormat.Rgb8:
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < 3; c++)
                        row[x * 3 + c] = To8(floats[x * 4 + c]);
                }

                return;
            case PixelFormat.Rgb16:
                for (var x = 0; x < width; x++)
                {
                    for (var c = 0; c < 3; c++)
                        BinaryPrimitives.WriteUInt16LittleEndian(row[((x * 3 + c) * 2)..], To16(floats[x * 4 + c]));
                }

                return;
            case PixelFormat.Gray8:
                for (var x = 0; x < width; x++)
                    row[x] = To8(floats[x * 4]);
                return;
            default:
                for (var x = 0; x < width; x++)
                    BinaryPrimitives.WriteUInt16LittleEndian(row[(x * 2)..], To16(floats[x * 4]));
                return;
        }
    }

    /// <summary>
    ///     True when no pixel is translucent: always for a format without alpha, otherwise a scan that stops at the first
    ///     alpha below the top. What lets an operation skip premultiplying, which on opaque pixels changes nothing.
    /// </summary>
    public static bool IsOpaque(PixelBuffer image)
    {
        if (!image.Format.HasAlpha())
            return true;
        if (image.Format == PixelFormat.Rgba8)
            return IsOpaqueRgba8(image.Pixels);

        var floats = image.Format == PixelFormat.RgbaFloat;
        for (var y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);
            for (var x = 0; x < image.Width; x++)
            {
                var opaque = floats
                    ? MemoryMarshal.Cast<byte, float>(row)[x * 4 + 3] >= 1f
                    : MemoryMarshal.Cast<byte, ushort>(row)[x * 4 + 3] == ushort.MaxValue;
                if (!opaque)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Colour times alpha over the scale, pixel by pixel: the form in which a weighted sum does not darken edges.</summary>
    public static void Premultiply(Span<float> floats, float scale)
    {
        var inverse = 1f / scale;
        for (var i = 0; i < floats.Length; i += 4)
        {
            var a = floats[i + 3] * inverse;
            floats[i] *= a;
            floats[i + 1] *= a;
            floats[i + 2] *= a;
        }
    }

    /// <summary>Back to straight colour; where the sum left no alpha, the colour is zero, as nothing of it shows.</summary>
    public static void Unpremultiply(Span<float> floats, float scale)
    {
        for (var i = 0; i < floats.Length; i += 4)
        {
            var a = floats[i + 3];
            var factor = a > scale * 1e-6f ? scale / a : 0f;
            floats[i] *= factor;
            floats[i + 1] *= factor;
            floats[i + 2] *= factor;
        }
    }

    public static byte To8(float value) => (byte)Math.Clamp(value + 0.5f, 0f, 255f);

    public static ushort To16(float value) => (ushort)Math.Clamp(value + 0.5f, 0f, 65535f);

    private static bool IsOpaqueRgba8(ReadOnlySpan<byte> rgba)
    {
        var words = MemoryMarshal.Cast<byte, uint>(rgba);
        var i = 0;
        if (Vector.IsHardwareAccelerated && words.Length >= Vector<uint>.Count)
        {
            var vectors = MemoryMarshal.Cast<uint, Vector<uint>>(words);
            var alpha = new Vector<uint>(0xFF000000u);
            foreach (var v in vectors)
            {
                if ((v & alpha) != alpha)
                    return false;
            }

            i = vectors.Length * Vector<uint>.Count;
        }

        for (; i < words.Length; i++)
        {
            if (words[i] >> 24 != 0xFF)
                return false;
        }

        return true;
    }

    private static void WidenRgba8(ReadOnlySpan<byte> bytes, Span<float> floats)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && bytes.Length >= Vector<byte>.Count)
        {
            var vectors = MemoryMarshal.Cast<byte, Vector<byte>>(bytes);
            var outVectors = MemoryMarshal.Cast<float, Vector<float>>(floats);
            var o = 0;
            foreach (var v in vectors)
            {
                Vector.Widen(v, out var low16, out var high16);
                Vector.Widen(low16, out var a, out var b);
                Vector.Widen(high16, out var c, out var d);
                outVectors[o++] = Vector.ConvertToSingle(a);
                outVectors[o++] = Vector.ConvertToSingle(b);
                outVectors[o++] = Vector.ConvertToSingle(c);
                outVectors[o++] = Vector.ConvertToSingle(d);
            }

            i = vectors.Length * Vector<byte>.Count;
        }

        for (; i < bytes.Length; i++)
            floats[i] = bytes[i];
    }

    // Floats back to bytes, rounded and clamped: a Lanczos sum can overshoot either end.
    private static void NarrowRgba8(ReadOnlySpan<float> floats, Span<byte> bytes)
    {
        floats = floats[..bytes.Length];
        var i = 0;
        if (Vector.IsHardwareAccelerated && floats.Length >= Vector<float>.Count * 4)
        {
            var vectors = MemoryMarshal.Cast<float, Vector<float>>(floats);
            var outVectors = MemoryMarshal.Cast<byte, Vector<byte>>(bytes);
            var half = new Vector<float>(0.5f);
            var max = new Vector<float>(255f);
            var o = 0;
            for (var v = 0; v + 3 < vectors.Length; v += 4)
            {
                var a = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v] + half, Vector<float>.Zero), max));
                var b = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 1] + half, Vector<float>.Zero), max));
                var c = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 2] + half, Vector<float>.Zero), max));
                var d = Vector.ConvertToUInt32(Vector.Min(Vector.Max(vectors[v + 3] + half, Vector<float>.Zero), max));
                var ab = Vector.Narrow(a, b);
                var cd = Vector.Narrow(c, d);
                outVectors[o++] = Vector.Narrow(ab, cd);
            }

            i = o * Vector<byte>.Count;
        }

        for (; i < floats.Length; i++)
            bytes[i] = To8(floats[i]);
    }
}
