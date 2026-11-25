using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     8-bit RGBA to PNG. An image whose every pixel is opaque is written as RGB, a quarter smaller for
///     the same pixels; each row takes the filter that leaves it smallest to deflate, by the usual
///     minimum-sum heuristic. The five candidates are computed into five buffers and the winner handed
///     to the deflater as it is; filtered rows gather in a staging buffer so the deflater is called a
///     few times per image rather than once per row, since each call costs a lock and a buffer round.
/// </summary>
public static class PngEncoder
{
    private const int StagingSize = 256 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static void Write(PixelBuffer image, Stream output, CompressionLevel level = CompressionLevel.Optimal)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(output);

        var opaque = IsOpaque(image.Pixels);
        var channels = opaque ? 3 : 4;
        var rowBytes = image.Width * channels;
        var filteredRowBytes = rowBytes + 1;

        output.Write(Signature);
        WriteHeader(output, image.Width, image.Height, opaque ? PngColorType.Rgb : PngColorType.Rgba);

        using var idat = new PngIdatWriter(output);
        var pool = ArrayPool<byte>.Shared;
        var prior = pool.Rent(rowBytes);
        var raw = pool.Rent(rowBytes);
        var candidates = new byte[5][];
        for (var i = 0; i < 5; i++)
            candidates[i] = pool.Rent(filteredRowBytes);
        var stagingLength = Math.Max(StagingSize, filteredRowBytes);
        var staging = pool.Rent(stagingLength);
        try
        {
            using (var deflater = new ZLibStream(idat, level, leaveOpen: true))
            {
                Array.Clear(prior, 0, rowBytes);
                var staged = 0;
                for (var y = 0; y < image.Height; y++)
                {
                    var rawRow = raw.AsSpan(0, rowBytes);
                    if (opaque)
                        DropAlpha(image.Row(y), rawRow);
                    else
                        image.Row(y).CopyTo(rawRow);

                    var best = ChooseFilter(rawRow, prior.AsSpan(0, rowBytes), channels, candidates);
                    if (staged + filteredRowBytes > stagingLength)
                    {
                        deflater.Write(staging, 0, staged);
                        staged = 0;
                    }

                    candidates[best].AsSpan(0, filteredRowBytes).CopyTo(staging.AsSpan(staged));
                    staged += filteredRowBytes;

                    (prior, raw) = (raw, prior);
                }

                if (staged > 0)
                    deflater.Write(staging, 0, staged);
            }

            idat.Finish();
        }
        finally
        {
            pool.Return(prior);
            pool.Return(raw);
            foreach (var candidate in candidates)
                pool.Return(candidate);
            pool.Return(staging);
        }

        PngChunkWriter.Write(output, PngChunk.Iend, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteHeader(Stream output, int width, int height, PngColorType colorType)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = (byte)colorType;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        PngChunkWriter.Write(output, PngChunk.Ihdr, ihdr);
    }

    /// <summary>
    ///     Computes the five filters into <paramref name="candidates" /> (filter byte first, then the row)
    ///     and returns the index of the one with the smallest sum of absolute values, the bytes read as
    ///     signed: the heuristic libpng uses.
    /// </summary>
    private static int ChooseFilter(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> prior, int step, byte[][] candidates)
    {
        var bestType = PngFilter.None;
        var bestScore = long.MaxValue;
        for (var type = PngFilter.None; type <= PngFilter.Paeth; type++)
        {
            var candidate = candidates[type];
            candidate[0] = type;
            var filtered = candidate.AsSpan(1, raw.Length);
            PngFilter.Filter(type, raw, prior, step, filtered);
            var score = SumOfAbsoluteValues(filtered);
            if (score < bestScore)
            {
                bestScore = score;
                bestType = type;
            }
        }

        return bestType;
    }

    /// <summary>Σ |b| with the bytes read as signed, widened to 16 then 32 bits so no lane overflows.</summary>
    private static long SumOfAbsoluteValues(ReadOnlySpan<byte> bytes)
    {
        long sum = 0;
        var i = 0;
        if (Vector.IsHardwareAccelerated && bytes.Length >= Vector<byte>.Count)
        {
            var vectors = MemoryMarshal.Cast<byte, Vector<sbyte>>(bytes);
            var total = Vector<int>.Zero;
            foreach (var v in vectors)
            {
                Vector.Widen(v, out var low, out var high);
                var magnitudes = Vector.Abs(low) + Vector.Abs(high); // each ≤ 128, the sum ≤ 256: fits a short
                Vector.Widen(magnitudes, out var a, out var b);
                total += a + b;
            }

            sum = Vector.Sum(total);
            i = vectors.Length * Vector<byte>.Count;
        }

        for (; i < bytes.Length; i++)
        {
            var b = bytes[i];
            sum += b < 128 ? b : 256 - b;
        }

        return sum;
    }

    /// <summary>RGBA to RGB: sixteen bytes in, twelve out, through one byte shuffle where SSSE3 is there.</summary>
    private static unsafe void DropAlpha(ReadOnlySpan<byte> rgba, Span<byte> rgb)
    {
        var pixels = rgb.Length / 3;
        var p = 0;
        if (System.Runtime.Intrinsics.X86.Ssse3.IsSupported && pixels >= 8)
        {
            fixed (byte* source = rgba)
            fixed (byte* destination = rgb)
            {
                var shuffle = System.Runtime.Intrinsics.Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80);
                // Each step writes 16 bytes of which 12 are the pixels: the last four are overwritten by
                // the next step, so stop while a full 16-byte store still fits in the row.
                for (; (p + 4) * 3 + 4 <= rgb.Length; p += 4)
                {
                    var v = System.Runtime.Intrinsics.X86.Sse2.LoadVector128(source + p * 4);
                    System.Runtime.Intrinsics.X86.Sse2.Store(destination + p * 3, System.Runtime.Intrinsics.X86.Ssse3.Shuffle(v, shuffle));
                }
            }
        }

        for (var d = p * 3; d < rgb.Length; d += 3, p++)
        {
            var s = p * 4;
            rgb[d] = rgba[s];
            rgb[d + 1] = rgba[s + 1];
            rgb[d + 2] = rgba[s + 2];
        }
    }

    /// <summary>True when every alpha byte is 255: checked a vector of pixels at a time.</summary>
    private static bool IsOpaque(ReadOnlySpan<byte> rgba)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && rgba.Length >= Vector<uint>.Count * 4)
        {
            var pixels = MemoryMarshal.Cast<byte, uint>(rgba);
            var vectors = MemoryMarshal.Cast<uint, Vector<uint>>(pixels);
            var alphaMask = new Vector<uint>(0xFF000000u);
            var all = Vector<uint>.Zero;
            foreach (var v in vectors)
                all |= ~v & alphaMask;
            if (all != Vector<uint>.Zero)
                return false;
            i = vectors.Length * Vector<uint>.Count * 4;
        }

        for (i += 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255)
                return false;
        }

        return true;
    }
}
