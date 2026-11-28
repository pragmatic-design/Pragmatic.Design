using System.Buffers.Binary;
using System.IO.Compression;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     Reads a 16-bit, non-interlaced RGB or RGBA PNG to RGBA samples, straight from the specification: chunks, inflate,
///     the five filters. Written for the tests because Skia decodes 16-bit PNG only to half floats, which cannot hold a
///     16-bit sample exactly; the tests check it on Skia's own files before trusting it on the module's.
/// </summary>
internal static class SixteenBitPngReader
{
    public static (ushort[] Samples, int Width, int Height) Read(byte[] png)
    {
        int width = 0, height = 0, colorType = 0;
        using var idat = new MemoryStream();
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 16 || data[12] != 0 || (data[9] != 2 && data[9] != 6))
                    throw new InvalidOperationException($"Not a 16-bit, non-interlaced RGB or RGBA PNG: depth {data[8]}, type {data[9]}, interlace {data[12]}.");
                colorType = data[9];
            }
            else if (type == "IDAT")
                idat.Write(data);

            at += 12 + length;
        }

        var channels = colorType == 6 ? 4 : 3;
        var bpp = channels * 2;
        var stride = width * bpp;
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var inflater = new ZLibStream(idat, CompressionMode.Decompress))
            inflater.ReadExactly(raw);

        var samples = new ushort[width * height * 4];
        var prior = new byte[stride];
        var row = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            raw.AsSpan(y * (stride + 1) + 1, stride).CopyTo(row);
            for (var i = 0; i < stride; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0, b = prior[i], c = i >= bpp ? prior[i - bpp] : 0;
                row[i] += filter switch
                {
                    0 => 0,
                    1 => (byte)a,
                    2 => (byte)b,
                    3 => (byte)((a + b) / 2),
                    4 => (byte)Paeth(a, b, c),
                    _ => throw new InvalidOperationException($"Filter {filter}."),
                };
            }

            for (var x = 0; x < width; x++)
            {
                for (var ch = 0; ch < 4; ch++)
                {
                    samples[(y * width + x) * 4 + ch] = ch < channels
                        ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(x * bpp + ch * 2))
                        : ushort.MaxValue;
                }
            }

            (prior, row) = (row, prior);
        }

        return (samples, width, height);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
