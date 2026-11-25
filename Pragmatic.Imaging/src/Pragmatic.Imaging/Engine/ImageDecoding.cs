using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Jpeg;
using Pragmatic.Imaging.Codecs.Metadata;
using Pragmatic.Imaging.Codecs.Png;

namespace Pragmatic.Imaging.Engine;

/// <summary>
///     Reads a file with the engine that handles it: PNG and JPEG with the managed core, every other
///     format with the native library, and a JPEG whose frame the managed decoder does not handle (CMYK,
///     12-bit, arithmetic coding) with the native library too, chosen from the frame header. A JPEG or
///     PNG is returned the way it is shown, its EXIF orientation applied whichever engine decoded it, and
///     <see cref="Identify" /> reports that size, so the limits are checked against the image that will
///     exist.
/// </summary>
internal static class ImageDecoding
{
    /// <summary>The size and format from the header alone. The bytes may be the start of the file.</summary>
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        var format = FormatSniffer.Detect(data);
        if (format == ImageFormat.Unknown)
            return NativeCodec.Identify(data);

        (int Width, int Height) size;
        try
        {
            size = format == ImageFormat.Png ? PngDecoder.Identify(data) : JpegDecoder.Identify(data);
        }
        catch (ImageDecodeException e)
        {
            throw Failed(e);
        }

        return ExifOrientation.SwapsAxes(ExifOrientation.Read(data))
            ? new ImageInfo((uint)size.Height, (uint)size.Width, format)
            : new ImageInfo((uint)size.Width, (uint)size.Height, format);
    }

    /// <summary>Decodes the whole file. <paramref name="format" /> is what <see cref="Identify" /> answered.</summary>
    public static PixelBuffer Decode(ReadOnlyMemory<byte> data, ImageFormat format)
    {
        var span = data.Span;
        PixelBuffer image;
        try
        {
            image = format switch
            {
                ImageFormat.Png => PngDecoder.Decode(data),
                ImageFormat.Jpeg when JpegDecoder.CanDecode(span) => JpegDecoder.Decode(data),
                _ => NativeCodec.Decode(span),
            };
        }
        catch (ImageDecodeException e)
        {
            throw Failed(e);
        }
        catch (InvalidDataException e)
        {
            // The PNG decoder inflates through ZLibStream, which reports corrupt deflate data this way.
            throw Failed(e);
        }

        return format is ImageFormat.Png or ImageFormat.Jpeg
            ? ExifOrientation.Apply(image, ExifOrientation.Read(span))
            : image;
    }

    /// <summary>The core reports a file it cannot read with its own exception; callers of this module get the module's.</summary>
    private static ImagingException Failed(Exception e) => new(e.Message, e, ImagingError.DecodeFailed);
}
