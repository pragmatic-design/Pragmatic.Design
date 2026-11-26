using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The lossless encoder (RFC 9649). An image of more than 256 colours: subtract green, predict every
///     pixel from the one above, and prefix-code the residues, with runs of a repeated pixel written as
///     backward references of distance 1. That is the shape of the encoder the native library used
///     (image-webp 0.2.4), with every stage over the whole image a vector at a time. An image of 256 colours
///     or fewer, which that encoder wrote the same way, is written with the colour-indexing transform.
/// </summary>
internal static class Vp8LEncoder
{
    private const int LiteralCodes = 256;
    private const int LengthCodes = 24;
    private const int MaxRun = 4096;

    /// <summary>The distance code of the pixel to the left, (1, 0) in the format's table of neighbours.</summary>
    private const int LeftDistanceSymbol = 1;

    /// <summary>
    ///     Writes the <c>VP8L</c> payload of <paramref name="image" /> to <paramref name="writer" />; without
    ///     <paramref name="header" />, the stream an <c>ALPH</c> chunk holds, whose dimensions are the frame's.
    /// </summary>
    public static void Encode(PixelBuffer image, Vp8LBitWriter writer, bool header = true)
    {
        Span<uint> palette = stackalloc uint[Vp8LPalette.MaxColours];
        var colours = Vp8LPalette.Collect(image.Pixels, palette);
        if (colours == 0)
        {
            EncodePredicted(image, writer, header);
            return;
        }

        // On a small image the colour table can cost more than it saves, and writing it both ways
        // costs little there: the shorter is kept. A large one always gains from the table.
        if (image.Width * image.Height >= SmallImagePixels)
        {
            EncodeIndexed(image, palette[..colours], writer, header);
            return;
        }

        using var indexed = new Vp8LBitWriter(image.ByteLength / 2);
        EncodeIndexed(image, palette[..colours], indexed, header);
        indexed.Flush();
        using var predicted = new Vp8LBitWriter(image.ByteLength / 2);
        EncodePredicted(image, predicted, header);
        predicted.Flush();
        writer.WriteBytes(indexed.Written.Length <= predicted.Written.Length ? indexed.Written : predicted.Written);
    }

    /// <summary>Below this many pixels an image of few colours is written both ways, and the shorter kept.</summary>
    private const int SmallImagePixels = 65536;

    /// <summary>
    ///     At most 256 colours: the colour-indexing transform alone, its table delta-coded, and the indices
    ///     packed and prefix-coded with runs.
    /// </summary>
    private static void EncodeIndexed(PixelBuffer image, ReadOnlySpan<uint> palette, Vp8LBitWriter writer, bool header)
    {
        var width = image.Width;
        var height = image.Height;
        var bits = Vp8LPalette.PackingBits(palette.Length);
        var packedWidth = Vp8LInverseTransforms.DivideRoundingUp(width, bits);
        var packed = ArrayPool<uint>.Shared.Rent(packedWidth * height);
        try
        {
            var opaque = true;
            foreach (var colour in palette)
                opaque &= colour >> 24 == 0xFF;
            if (header)
                WriteHeader(writer, width, height, opaque);

            writer.Write(1, 1);
            writer.Write(3, 2);
            writer.Write((uint)palette.Length - 1, 8);
            Span<uint> deltas = stackalloc uint[palette.Length];
            deltas[0] = palette[0];
            for (var i = 1; i < palette.Length; i++)
                deltas[i] = Vp8LInverseTransforms.Subtract(palette[i], palette[i - 1]);
            writer.Write(0, 1); // the table has no colour cache
            WritePixels(writer, deltas);
            writer.Write(0, 1); // no more transforms

            var indices = packed.AsSpan(0, packedWidth * height);
            Vp8LPalette.Bundle(image.Pixels, width, height, palette, bits, indices);
            writer.Write(0, 1); // no colour cache
            writer.Write(0, 1); // one group of codes
            WritePixels(writer, indices);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(packed);
        }
    }

    private static void WriteHeader(Vp8LBitWriter writer, int width, int height, bool opaque)
    {
        writer.Write(0x2F, 8);
        writer.Write((uint)width - 1, 14);
        writer.Write((uint)height - 1, 14);
        writer.Write(opaque ? 0u : 1u, 1);
        writer.Write(0, 3);
    }

    /// <summary>More than 256 colours: subtract green and the predictor, then the residues prefix-coded with runs.</summary>
    private static void EncodePredicted(PixelBuffer image, Vp8LBitWriter writer, bool header)
    {
        var width = image.Width;
        var height = image.Height;
        var count = width * height;
        var argb = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            var pixels = argb.AsSpan(0, count);
            var opaque = ToResidues(image.Pixels, pixels, width, height);
            if (header)
                WriteHeader(writer, width, height, opaque);

            // Subtract green (type 2), then the predictor (type 0) with one block for the whole image
            // at the largest size, 512, all of mode 2: the pixel above.
            writer.Write(1, 1);
            writer.Write(2, 2);
            writer.Write(1, 1);
            writer.Write(0, 2);
            writer.Write(9 - 2, 3);
            writer.Write(0, 1); // the mode image has no colour cache
            WriteSingle(writer, 2);
            for (var i = 0; i < 4; i++)
                WriteSingle(writer, 0);
            writer.Write(0, 1); // no more transforms

            writer.Write(0, 1); // no colour cache
            writer.Write(0, 1); // one group of codes

            WritePixels(writer, pixels);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(argb);
        }
    }

    /// <summary>
    ///     RGBA to ARGB, green subtracted from red and blue, then each pixel minus the one above (the first
    ///     row minus the one to the left, the first pixel minus opaque black). Returns whether every pixel
    ///     is opaque. Per-channel subtraction modulo 256 is byte-wise subtraction.
    /// </summary>
    private static bool ToResidues(ReadOnlySpan<byte> rgba, Span<uint> pixels, int width, int height)
    {
        var bytes = MemoryMarshal.AsBytes(pixels);
        rgba.CopyTo(bytes);
        var opaque = true;
        var i = 0;
        var count = Vector<uint>.Count;
        var low = new Vector<uint>(0xFF);
        var alphaMask = new Vector<uint>(0xFF000000);
        var opaqueSoFar = Vector<uint>.AllBitsSet;
        for (; i + count <= pixels.Length; i += count)
        {
            // R and B swap places (RGBA bytes to an ARGB word), then green comes off both.
            var v = new Vector<uint>(pixels[i..]);
            var red = v & low;
            var blue = Vector.ShiftRightLogical(v, 16) & low;
            v = (v & new Vector<uint>(0xFF00FF00)) | Vector.ShiftLeft(red, 16) | blue;
            var green = Vector.ShiftRightLogical(v, 8) & low;
            v = Vector.AsVectorUInt32(Vector.AsVectorByte(v) - Vector.AsVectorByte(Vector.ShiftLeft(green, 16) | green));
            opaqueSoFar &= Vector.Equals(v & alphaMask, alphaMask);
            v.CopyTo(pixels[i..]);
        }

        opaque = opaqueSoFar == Vector<uint>.AllBitsSet;
        for (; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var argb = (p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF);
            var green = (argb >> 8) & 0xFF;
            pixels[i] = Vp8LInverseTransforms.Subtract(argb, (green << 16) | green);
            opaque &= argb >> 24 == 0xFF;
        }

        // Bottom-up, so each row still has the row above as it was.
        for (var y = height - 1; y > 0; y--)
            SubtractRow(pixels.Slice(y * width, width), pixels.Slice((y - 1) * width, width));

        for (var x = width - 1; x > 0; x--)
            pixels[x] = Vp8LInverseTransforms.Subtract(pixels[x], pixels[x - 1]);
        pixels[0] = Vp8LInverseTransforms.Subtract(pixels[0], 0xFF000000);
        return opaque;
    }

    private static void SubtractRow(Span<uint> row, ReadOnlySpan<uint> above)
    {
        var i = 0;
        for (; i + Vector<uint>.Count <= row.Length; i += Vector<uint>.Count)
            Vector.AsVectorUInt32(Vector.AsVectorByte(new Vector<uint>(row[i..])) - Vector.AsVectorByte(new Vector<uint>(above[i..]))).CopyTo(row[i..]);
        for (; i < row.Length; i++)
            row[i] = Vp8LInverseTransforms.Subtract(row[i], above[i]);
    }

    private static void WritePixels(Vp8LBitWriter writer, ReadOnlySpan<uint> pixels)
    {
        var frequencies = ArrayPool<uint>.Shared.Rent(LiteralCodes + LengthCodes + 3 * LiteralCodes);
        var lengths = ArrayPool<byte>.Shared.Rent(LiteralCodes + LengthCodes + 3 * LiteralCodes);
        var codes = ArrayPool<ushort>.Shared.Rent(LiteralCodes + LengthCodes + 3 * LiteralCodes);
        try
        {
            var greenFrequencies = frequencies.AsSpan(0, LiteralCodes + LengthCodes);
            var redFrequencies = frequencies.AsSpan(LiteralCodes + LengthCodes, LiteralCodes);
            var blueFrequencies = frequencies.AsSpan(2 * LiteralCodes + LengthCodes, LiteralCodes);
            var alphaFrequencies = frequencies.AsSpan(3 * LiteralCodes + LengthCodes, LiteralCodes);
            frequencies.AsSpan(0, LiteralCodes + LengthCodes + 3 * LiteralCodes).Clear();
            Count(pixels, greenFrequencies, redFrequencies, blueFrequencies, alphaFrequencies);

            var green = new Code(lengths.AsSpan(0, LiteralCodes + LengthCodes), codes.AsSpan(0, LiteralCodes + LengthCodes));
            var red = new Code(lengths.AsSpan(LiteralCodes + LengthCodes, LiteralCodes), codes.AsSpan(LiteralCodes + LengthCodes, LiteralCodes));
            var blue = new Code(lengths.AsSpan(2 * LiteralCodes + LengthCodes, LiteralCodes), codes.AsSpan(2 * LiteralCodes + LengthCodes, LiteralCodes));
            var alpha = new Code(lengths.AsSpan(3 * LiteralCodes + LengthCodes, LiteralCodes), codes.AsSpan(3 * LiteralCodes + LengthCodes, LiteralCodes));
            Vp8LCodeWriter.Write(writer, greenFrequencies, green.Lengths, green.Codes);
            Vp8LCodeWriter.Write(writer, redFrequencies, red.Lengths, red.Codes);
            Vp8LCodeWriter.Write(writer, blueFrequencies, blue.Lengths, blue.Codes);
            Vp8LCodeWriter.Write(writer, alphaFrequencies, alpha.Lengths, alpha.Codes);
            WriteSingle(writer, LeftDistanceSymbol);

            Emit(writer, pixels, green, red, blue, alpha);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(frequencies);
            ArrayPool<byte>.Shared.Return(lengths);
            ArrayPool<ushort>.Shared.Return(codes);
        }
    }

    private static void Count(ReadOnlySpan<uint> pixels, Span<uint> green, Span<uint> red, Span<uint> blue, Span<uint> alpha)
    {
        var i = 0;
        while (i < pixels.Length)
        {
            var p = pixels[i++];
            green[(int)(p >> 8) & 0xFF]++;
            red[(int)(p >> 16) & 0xFF]++;
            blue[(int)p & 0xFF]++;
            alpha[(int)(p >> 24)]++;
            var run = i < pixels.Length && pixels[i] == p ? Run(pixels, i, p) : 0;
            if (run > 0)
            {
                green[LiteralCodes + Prefix(run, out _, out _)]++;
                i += run;
            }
        }
    }

    private static void Emit(Vp8LBitWriter writer, ReadOnlySpan<uint> pixels, Code green, Code red, Code blue, Code alpha)
    {
        // Each literal symbol's code and length in one word (length above bit 16): one load per channel.
        Span<uint> greenTable = stackalloc uint[LiteralCodes];
        Span<uint> redTable = stackalloc uint[LiteralCodes];
        Span<uint> blueTable = stackalloc uint[LiteralCodes];
        Span<uint> alphaTable = stackalloc uint[LiteralCodes];
        for (var s = 0; s < LiteralCodes; s++)
        {
            greenTable[s] = green.Codes[s] | ((uint)green.Lengths[s] << 16);
            redTable[s] = red.Codes[s] | ((uint)red.Lengths[s] << 16);
            blueTable[s] = blue.Codes[s] | ((uint)blue.Lengths[s] << 16);
            alphaTable[s] = alpha.Codes[s] | ((uint)alpha.Lengths[s] << 16);
        }

        var i = 0;
        while (i < pixels.Length)
        {
            var p = pixels[i++];
            var g = greenTable[(int)(p >> 8) & 0xFF];
            var r = redTable[(int)(p >> 16) & 0xFF];
            var b = blueTable[(int)p & 0xFF];
            var a = alphaTable[(int)(p >> 24)];
            var greenLength = (int)(g >> 16);
            var blueLength = (int)(b >> 16);
            writer.Write((g & 0xFFFF) | ((r & 0xFFFF) << greenLength), greenLength + (int)(r >> 16));
            writer.Write((b & 0xFFFF) | ((a & 0xFFFF) << blueLength), blueLength + (int)(a >> 16));
            var run = i < pixels.Length && pixels[i] == p ? Run(pixels, i, p) : 0;
            if (run > 0)
            {
                var symbol = LiteralCodes + Prefix(run, out var extraBits, out var extra);
                writer.Write(green.Codes[symbol], green.Lengths[symbol]);
                writer.Write((uint)extra, extraBits);
                i += run;
            }
        }
    }

    /// <summary>How many pixels from <paramref name="start" /> repeat <paramref name="pixel" />, up to the longest run the format codes.</summary>
    private static int Run(ReadOnlySpan<uint> pixels, int start, uint pixel)
    {
        var end = Math.Min(pixels.Length, start + MaxRun);
        var i = start;
        while (i < end && pixels[i] == pixel)
            i++;
        return i - start;
    }

    /// <summary>The prefix symbol of a length or distance, with its extra bits (RFC 9649, section 5.2.2).</summary>
    private static int Prefix(int value, out int extraBits, out int extra)
    {
        var d = value - 1;
        if (d < 2)
        {
            extraBits = 0;
            extra = 0;
            return d;
        }

        var highest = BitOperations.Log2((uint)d);
        var second = (d >> (highest - 1)) & 1;
        extraBits = highest - 1;
        extra = d & ((1 << extraBits) - 1);
        return 2 * highest + second;
    }

    /// <summary>A simple code with one symbol, which takes no bits to read.</summary>
    private static void WriteSingle(Vp8LBitWriter writer, int symbol)
    {
        writer.Write(1, 1);
        writer.Write(0, 1);
        if (symbol <= 1)
        {
            writer.Write(0, 1);
            writer.Write((uint)symbol, 1);
        }
        else
        {
            writer.Write(1, 1);
            writer.Write((uint)symbol, 8);
        }
    }

    private readonly ref struct Code(Span<byte> lengths, Span<ushort> codes)
    {
        public Span<byte> Lengths { get; } = lengths;

        public Span<ushort> Codes { get; } = codes;
    }
}
