using Pragmatic.Imaging.Codecs.Tests.Workload;
using Pragmatic.Imaging.Codecs.Jpeg;
using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

/// <summary>
///     The planes against a plain scalar computation of the same formulas: the SIMD paths must agree
///     to the bit, on a width the vectors cover whole and on one with a scalar tail and an odd pixel.
/// </summary>
public class JpegPlanesTests
{
    [Theory]
    [InlineData(400, 267)]
    [InlineData(333, 211)]
    [InlineData(17, 3)]
    public void Planes_match_the_scalar_formulas(int width, int height)
    {
        var rgba = SyntheticPhoto.Rgba(width, height);
        using var image = new PixelBuffer(width, height);
        rgba.CopyTo(image.Pixels);

        using var planes = JpegPlanes.From(image);

        for (var y = 0; y < planes.LumaHeight; y++)
        {
            var sy = Math.Min(y, height - 1);
            for (var x = 0; x < planes.LumaStride; x++)
            {
                var sx = Math.Min(x, width - 1);
                var o = (sy * width + sx) * 4;
                var expected = (byte)((19595 * rgba[o] + 38470 * rgba[o + 1] + 7471 * rgba[o + 2] + 32768) >> 16);
                Assert.True(expected == planes.Luma[y * planes.LumaStride + x], $"Y at ({x}, {y}): {planes.Luma[y * planes.LumaStride + x]}, expected {expected}");
            }
        }

        var chromaWidth = (width + 1) / 2;
        for (var cy = 0; cy < planes.ChromaHeight; cy++)
        {
            var y0 = Math.Min(cy * 2, height - 1);
            var y1 = Math.Min(cy * 2 + 1, height - 1);
            for (var cx = 0; cx < planes.ChromaStride; cx++)
            {
                var scx = Math.Min(cx, chromaWidth - 1);
                var x0 = scx * 2;
                var x1 = Math.Min(scx * 2 + 1, width - 1);
                var r = 0;
                var g = 0;
                var b = 0;
                foreach (var (sy, sx) in new[] { (y0, x0), (y0, x1), (y1, x0), (y1, x1) })
                {
                    var o = (sy * width + sx) * 4;
                    r += rgba[o];
                    g += rgba[o + 1];
                    b += rgba[o + 2];
                }

                var cb = (byte)Math.Clamp((-11056 * r - 21712 * g + 32768 * b + (512 << 16) + (2 << 16)) >> 18, 0, 255);
                var cr = (byte)Math.Clamp((32768 * r - 27440 * g - 5328 * b + (512 << 16) + (2 << 16)) >> 18, 0, 255);
                var at = cy * planes.ChromaStride + cx;
                Assert.True(cb == planes.Cb[at], $"Cb at ({cx}, {cy}): {planes.Cb[at]}, expected {cb}");
                Assert.True(cr == planes.Cr[at], $"Cr at ({cx}, {cy}): {planes.Cr[at]}, expected {cr}");
            }
        }
    }
}
