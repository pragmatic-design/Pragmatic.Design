namespace Pragmatic.Imaging.Codecs.Tests.Workload;

/// <summary>
///     A deterministic, photo-like RGBA image: smooth gradients (what JPEG compresses well), fine noise
///     (what it does not), and hard-edged shapes (what a resampler has to get right). No file in the
///     repository, the same bytes on every machine.
/// </summary>
public static class SyntheticPhoto
{
    /// <summary>Fills <paramref name="width" /> × <paramref name="height" /> RGBA pixels, alpha opaque.</summary>
    public static byte[] Rgba(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var noise = 0x9E3779B9u;

        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            var v = (double)y / height;
            for (var x = 0; x < width; x++)
            {
                var u = (double)x / width;

                // Sky-to-ground gradient with a diagonal band.
                var r = 40 + 150 * u;
                var g = 60 + 120 * (1 - v);
                var b = 110 + 100 * Math.Sin((u + v) * Math.PI);

                // Fine noise, xorshift: a few levels, like sensor grain.
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                var grain = (int)(noise & 7) - 4;

                // Hard edges: a grid of rectangles and one disc.
                var inRectangle = (x / 97 + y / 61) % 7 == 0 && x % 97 < 60 && y % 61 < 30;
                var dx = x - width * 0.7;
                var dy = y - height * 0.35;
                var inDisc = dx * dx + dy * dy < width * width * 0.01;

                if (inRectangle) { r = 230; g = 200; b = 40; }
                if (inDisc) { r = 220; g = 50; b = 60; }

                var i = row + x * 4;
                pixels[i] = Clamp(r + grain);
                pixels[i + 1] = Clamp(g + grain);
                pixels[i + 2] = Clamp(b + grain);
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);
}
