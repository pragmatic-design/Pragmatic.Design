using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging.Engine;

/// <summary>
///     The native library, for the formats the managed core does not read or write yet: WebP, AVIF, GIF,
///     BMP and TIFF, and a JPEG whose frame the managed decoder does not handle (CMYK, 12-bit, arithmetic
///     coding). Decoded pixels are copied into a <see cref="PixelBuffer" /> and the native buffer freed at
///     once, so every operation after a load runs managed whatever engine decoded the file.
/// </summary>
internal static unsafe class NativeCodec
{
    public static ImageInfo Identify(ReadOnlySpan<byte> data)
    {
        uint width, height;
        int format;
        fixed (byte* pointer = data)
        {
            var rc = NativeImports.pragmatic_image_info(pointer, (nuint)data.Length, &width, &height, &format);
            NativeError.CheckResult(rc, "image_info");
        }

        return new ImageInfo(width, height, (ImageFormat)format);
    }

    public static PixelBuffer Decode(ReadOnlySpan<byte> data)
    {
        nint pixels;
        nuint length;
        uint width, height;
        fixed (byte* pointer = data)
        {
            var rc = NativeImports.pragmatic_image_decode(pointer, (nuint)data.Length, &pixels, &length, &width, &height);
            NativeError.CheckResult(rc, "image_decode");
        }

        using var native = new NativeBuffer(pixels, length);
        var image = new PixelBuffer((int)width, (int)height);
        native.AsSpan().CopyTo(image.Pixels);
        return image;
    }

    public static NativeBuffer Encode(PixelBuffer image, ImageFormat format, byte quality)
    {
        nint output;
        nuint length;
        var rc = NativeImports.pragmatic_image_encode(image.Pointer, (nuint)image.ByteLength,
            (uint)image.Width, (uint)image.Height, (int)format, quality, &output, &length);
        NativeError.CheckResult(rc, "image_encode");
        return new NativeBuffer(output, length);
    }
}
