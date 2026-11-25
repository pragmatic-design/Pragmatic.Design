using Pragmatic.Imaging.Codecs.Png;

namespace Pragmatic.Imaging.Tests.Exif;

/// <summary>
///     Samples a pipeline's image a quarter and three quarters of the way down its middle column, far from
///     the red/blue edge where JPEG blurs the colours: on the upright fixture the first is red, the second
///     blue. The pixels are read through a lossless PNG encode and the managed PNG decoder.
/// </summary>
internal static class UprightPixels
{
    public readonly record struct Rgb(byte R, byte G, byte B);

    public static (Rgb Top, Rgb Bottom) Of(ImagePipeline pipeline)
    {
        using var image = PngDecoder.Decode(pipeline.Encode(ImageFormat.Png));
        var x = image.Width / 2;
        return (At(image.Row(image.Height / 4), x), At(image.Row(image.Height * 3 / 4), x));
    }

    public static bool IsRed(Rgb p) => p.R > 200 && p.B < 60;

    public static bool IsBlue(Rgb p) => p.B > 200 && p.R < 60;

    private static Rgb At(ReadOnlySpan<byte> row, int x) => new(row[x * 4], row[x * 4 + 1], row[x * 4 + 2]);
}
