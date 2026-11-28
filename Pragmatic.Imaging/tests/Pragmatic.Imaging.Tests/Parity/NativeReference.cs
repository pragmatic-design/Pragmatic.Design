using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging.Tests.Parity;

/// <summary>
///     The native library's operations, called through its P/Invoke signatures on raw RGBA: the reference
///     the managed operations are held to while the library still ships.
/// </summary>
internal static unsafe class NativeReference
{
    internal readonly record struct Image(byte[] Rgba, int Width, int Height);

    public delegate int Operation(byte* rgba, nuint length, uint width, uint height, nint* output, nuint* outputLength);

    public static Image Apply(Image source, Operation operation)
    {
        nint output;
        nuint length;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = operation(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height, &output, &length);
            NativeError.CheckResult(rc, "reference");
        }

        using var buffer = new NativeBuffer(output, length);
        return source with { Rgba = buffer.ToArray() };
    }

    public static Image Rotate(Image source, int degrees)
    {
        nint output;
        nuint length;
        uint width, height;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = NativeImports.pragmatic_image_rotate(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height,
                degrees, &output, &length, &width, &height);
            NativeError.CheckResult(rc, "reference rotate");
        }

        using var buffer = new NativeBuffer(output, length);
        return new Image(buffer.ToArray(), (int)width, (int)height);
    }

    /// <summary>The native WebP encoder, lossless whatever the quality: what the managed one replaced.</summary>
    public static byte[] EncodeWebP(Image source)
    {
        nint output;
        nuint length;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = NativeImports.pragmatic_image_encode(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height,
                (int)ImageFormat.WebP, 90, &output, &length);
            NativeError.CheckResult(rc, "reference encode");
        }

        using var buffer = new NativeBuffer(output, length);
        return buffer.ToArray();
    }

    /// <summary>The native JPEG encoder: what the managed one replaced, at the same quality setting.</summary>
    public static byte[] EncodeJpeg(Image source, byte quality)
    {
        nint output;
        nuint length;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = NativeImports.pragmatic_image_encode(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height,
                (int)ImageFormat.Jpeg, quality, &output, &length);
            NativeError.CheckResult(rc, "reference encode");
        }

        using var buffer = new NativeBuffer(output, length);
        return buffer.ToArray();
    }

    /// <summary>The native BMP encoder: what the managed one replaced.</summary>
    public static byte[] EncodeBmp(Image source)
    {
        nint output;
        nuint length;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = NativeImports.pragmatic_image_encode(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height,
                (int)ImageFormat.Bmp, 90, &output, &length);
            NativeError.CheckResult(rc, "reference encode");
        }

        using var buffer = new NativeBuffer(output, length);
        return buffer.ToArray();
    }

    /// <summary>The native decoder: what the managed one replaced, on the same file.</summary>
    public static Image Decode(byte[] encoded)
    {
        nint output;
        nuint length;
        uint width, height;
        fixed (byte* data = encoded)
        {
            var rc = NativeImports.pragmatic_image_decode(data, (nuint)encoded.Length, &output, &length, &width, &height);
            NativeError.CheckResult(rc, "reference decode");
        }

        using var buffer = new NativeBuffer(output, length);
        return new Image(buffer.ToArray(), (int)width, (int)height);
    }

    public static Image Crop(Image source, int x, int y, int width, int height)
    {
        nint output;
        nuint length;
        fixed (byte* pixels = source.Rgba)
        {
            var rc = NativeImports.pragmatic_image_crop(pixels, (nuint)source.Rgba.Length, (uint)source.Width, (uint)source.Height,
                (uint)x, (uint)y, (uint)width, (uint)height, &output, &length);
            NativeError.CheckResult(rc, "reference crop");
        }

        using var buffer = new NativeBuffer(output, length);
        return new Image(buffer.ToArray(), width, height);
    }
}
