// Follows libwebp (BSD-3-Clause, Copyright (c) 2010 Google Inc.): see THIRD-PARTY-NOTICES.md.
namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     The <c>ALPH</c> chunk beside a lossy frame, the inverse of <see cref="WebPAlpha" />: one byte of header,
///     then the alpha plane, unfiltered, as the green channel of a header-less lossless stream. Alpha is kept
///     exactly, as libwebp keeps it at its default alpha quality of 100.
/// </summary>
internal static class WebPAlphaEncoder
{
    /// <summary>Lossless compression, no filter, no pre-processing.</summary>
    private const byte Header = 1;

    /// <summary>Whether any pixel of <paramref name="rgba" /> is not opaque.</summary>
    public static bool HasAlpha(ReadOnlySpan<byte> rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255)
                return true;
        }

        return false;
    }

    /// <summary>The chunk's payload: the header byte and the stream.</summary>
    public static Vp8LBitWriter Encode(PixelBuffer image)
    {
        using var plane = new PixelBuffer(image.Width, image.Height);
        var source = image.Pixels;
        var target = plane.Pixels;
        for (var i = 0; i < source.Length; i += 4)
        {
            target[i] = 0;
            target[i + 1] = source[i + 3];
            target[i + 2] = 0;
            target[i + 3] = 255;
        }

        var writer = new Vp8LBitWriter(image.Width * image.Height / 4);
        try
        {
            writer.Write(Header, 8);
            Vp8LEncoder.Encode(plane, writer, header: false);
            writer.Flush();
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }
}
