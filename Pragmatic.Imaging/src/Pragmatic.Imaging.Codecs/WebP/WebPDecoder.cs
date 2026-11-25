namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     Decodes WebP to RGBA: lossless (VP8L), lossy (VP8) with or without an alpha plane, in the simple or
///     the extended container. An animation decodes to its first frame on a transparent canvas, the
///     picture libwebp's animation decoder gives for frame 0. Metadata chunks are not read.
/// </summary>
public static class WebPDecoder
{
    /// <summary>Whether the bytes start like a WebP file: <c>RIFF</c>, a size, <c>WEBP</c>.</summary>
    public static bool IsWebP(ReadOnlySpan<byte> encoded) => WebPContainer.HasSignature(encoded);

    /// <summary>The size of the image as shown. The bytes may be the start of the file: the first 30 are enough.</summary>
    /// <exception cref="ImageDecodeException">The bytes are not a WebP header.</exception>
    public static (int Width, int Height) Identify(ReadOnlySpan<byte> encoded) => WebPContainer.Identify(encoded);

    /// <summary>Decodes the image, or an animation's first frame.</summary>
    /// <exception cref="ImageDecodeException">The bytes are truncated, corrupt, or not WebP.</exception>
    public static PixelBuffer Decode(ReadOnlySpan<byte> encoded)
    {
        var layout = WebPContainer.Read(encoded);
        var image = encoded.Slice(layout.Image.Offset, layout.Image.Length);
        var result = new PixelBuffer(layout.CanvasWidth, layout.CanvasHeight);
        try
        {
            if (!layout.IsAnimated)
            {
                DecodeFrame(image, encoded, layout, result.Pixels);
                return result;
            }

            // Frame 0 is a key frame: the canvas starts transparent and the frame is written over it as
            // decoded, not blended (libwebp's anim_decode.c).
            result.Pixels.Clear();
            var (width, height) = FrameSize(layout, image);
            var frame = new byte[width * height * 4];
            DecodeFrame(image, encoded, layout, frame);
            for (var y = 0; y < height; y++)
                frame.AsSpan(y * width * 4, width * 4).CopyTo(result.Row(layout.FrameY + y)[(layout.FrameX * 4)..]);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void DecodeFrame(ReadOnlySpan<byte> image, ReadOnlySpan<byte> file, WebPLayout layout, Span<byte> rgba)
    {
        if (layout.IsLossless)
        {
            var argb = Vp8LDecoder.Decode(image, out _, out _);
            for (var i = 0; i < argb.Length; i++)
            {
                var pixel = argb[i];
                rgba[i * 4] = (byte)(pixel >> 16);
                rgba[i * 4 + 1] = (byte)(pixel >> 8);
                rgba[i * 4 + 2] = (byte)pixel;
                rgba[i * 4 + 3] = (byte)(pixel >> 24);
            }

            return;
        }

        var frame = Vp8Decoder.Decode(image);
        Vp8YuvConverter.ToRgba(frame, rgba);

        // An ALPH chunk counts only when the extended header says the image has alpha: libwebp's reading
        // (through Skia) and the native library's both leave the image opaque otherwise.
        if (layout.HasAlpha && layout.Alpha.Length > 0)
            WebPAlpha.Apply(file.Slice(layout.Alpha.Offset, layout.Alpha.Length), frame.Width, frame.Height, rgba);
    }

    private static (int Width, int Height) FrameSize(WebPLayout layout, ReadOnlySpan<byte> image)
    {
        if (!layout.IsLossless)
            return Vp8Decoder.ReadHeader(image);
        var (width, height, _) = Vp8LDecoder.ReadHeader(image);
        return (width, height);
    }
}
