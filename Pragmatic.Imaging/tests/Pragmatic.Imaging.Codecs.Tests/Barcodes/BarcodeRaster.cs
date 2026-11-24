using Pragmatic.Imaging.Codecs.Barcodes;

namespace Pragmatic.Imaging.Codecs.Tests.Barcodes;

/// <summary>Draws a symbol as black-on-white RGBA pixels with its quiet zone, for a decoder to read.</summary>
internal static class BarcodeRaster
{
    internal readonly record struct Image(byte[] Rgba, int Width, int Height);

    /// <summary>A QR code at <paramref name="scale" /> pixels per module, with the four-module quiet zone the standard asks.</summary>
    public static Image Qr(QrCode qr, int scale = 4)
    {
        const int quiet = 4;
        var side = (qr.Size + 2 * quiet) * scale;
        var rgba = White(side, side);
        for (var y = 0; y < qr.Size; y++)
        {
            for (var x = 0; x < qr.Size; x++)
            {
                if (qr[x, y])
                    Fill(rgba, side, (x + quiet) * scale, (y + quiet) * scale, scale, scale);
            }
        }

        return new Image(rgba, side, side);
    }

    /// <summary>A linear symbol from its bar and space widths (in modules, starting with a bar), with a ten-module quiet zone each side.</summary>
    public static Image Linear(int[] widths, int scale = 3, int height = 60)
    {
        const int quiet = 10;
        var width = (widths.Sum() + 2 * quiet) * scale;
        var rgba = White(width, height);
        var x = quiet * scale;
        for (var i = 0; i < widths.Length; i++)
        {
            var w = widths[i] * scale;
            if (i % 2 == 0)
                Fill(rgba, width, x, 0, w, height);
            x += w;
        }

        return new Image(rgba, width, height);
    }

    private static byte[] White(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        Array.Fill(rgba, (byte)255);
        return rgba;
    }

    private static void Fill(byte[] rgba, int stride, int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                var i = (y * stride + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = 0;
            }
        }
    }
}
