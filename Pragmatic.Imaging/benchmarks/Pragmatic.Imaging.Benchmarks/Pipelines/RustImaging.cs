using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging.Benchmarks.Pipelines;

/// <summary>
///     The native library's entry points, called the way Pragmatic.Imaging called them before its PNG and
///     JPEG paths went managed: decode to an RGBA buffer the library allocates, operate buffer to buffer,
///     encode. Every result is a <see cref="NativeBuffer" /> the caller frees.
/// </summary>
internal static unsafe class RustImaging
{
    public static NativeBuffer Decode(byte[] encoded, out uint width, out uint height)
    {
        nint pixels;
        nuint length;
        uint w, h;
        fixed (byte* data = encoded)
        {
            NativeError.CheckResult(NativeImports.pragmatic_image_decode(data, (nuint)encoded.Length, &pixels, &length, &w, &h), "image_decode");
        }

        (width, height) = (w, h);
        return new NativeBuffer(pixels, length);
    }

    public static NativeBuffer Resize(byte* rgba, nuint length, uint width, uint height, uint toWidth, uint toHeight)
    {
        nint output;
        nuint outputLength;
        NativeError.CheckResult(NativeImports.pragmatic_image_resize(rgba, length, width, height, toWidth, toHeight,
            (int)ResizeFilter.Lanczos3, &output, &outputLength), "image_resize");
        return new NativeBuffer(output, outputLength);
    }

    public static byte[] Encode(byte* rgba, nuint length, uint width, uint height, ImageFormat format, byte quality)
    {
        nint output;
        nuint outputLength;
        NativeError.CheckResult(NativeImports.pragmatic_image_encode(rgba, length, width, height, (int)format, quality, &output, &outputLength), "image_encode");
        using var buffer = new NativeBuffer(output, outputLength);
        return buffer.ToArray();
    }

    /// <summary>The thumbnail size Pragmatic.Imaging computed for a box, never upscaling.</summary>
    public static (uint Width, uint Height) ThumbnailSize(uint width, uint height, uint maxWidth, uint maxHeight)
    {
        var ratio = Math.Min(Math.Min((double)maxWidth / width, (double)maxHeight / height), 1.0);
        return ((uint)Math.Max(1, Math.Round(width * ratio)), (uint)Math.Max(1, Math.Round(height * ratio)));
    }
}
