using Pragmatic.Imaging.Codecs.Bmp;
using Pragmatic.Imaging.Codecs.Gif;
using Pragmatic.Imaging.Codecs.Tiff;
using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Engine;

/// <summary>
///     Whether encoded bytes are a PNG, a JPEG, a WebP, a BMP, a GIF or a TIFF, from their signature: the formats the managed
///     core reads. Anything else is <see cref="ImageFormat.Unknown" /> here and goes to the native library,
///     which names its own formats. Nothing past the first bytes is read, and no extension is trusted.
/// </summary>
internal static class FormatSniffer
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static ImageFormat Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(PngSignature))
            return ImageFormat.Png;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ImageFormat.Jpeg;
        if (WebPDecoder.IsWebP(data))
            return ImageFormat.WebP;
        if (BmpDecoder.IsBmp(data))
            return ImageFormat.Bmp;
        if (GifDecoder.IsGif(data))
            return ImageFormat.Gif;
        if (TiffDecoder.IsTiff(data))
            return ImageFormat.Tiff;
        return ImageFormat.Unknown;
    }
}
