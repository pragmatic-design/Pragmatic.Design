using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging;

public sealed partial class ImagePipeline
{
    /// <summary>Load an image from encoded bytes (any supported format).</summary>
    /// <param name="data">Encoded image bytes.</param>
    /// <param name="options">Safety limits. Defaults to <see cref="ImagingOptions.Default"/>.</param>
    public static ImagePipeline Load(ReadOnlySpan<byte> data, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        options.ValidateInput(data.Length);

        // Pre-validate dimensions via header read BEFORE full decode to prevent decompression bombs
        var info = ImageInfo.FromBytes(data);
        options.ValidateDecoded(info.Width, info.Height);
        options.ValidateFormat(info.Format);

        unsafe
        {
            nint outRgba;
            nuint outLen;
            uint w, h;

            fixed (byte* ptr = data)
            {
                var rc = NativeImports.pragmatic_image_decode(
                    ptr, (nuint)data.Length,
                    &outRgba, &outLen, &w, &h);
                NativeError.CheckResult(rc, "image_decode");
            }

            var buffer = new NativeBuffer(outRgba, outLen);
            var raw = RawImage.FromNative(buffer, w, h);
            return new ImagePipeline(raw);
        }
    }

    /// <summary>Load an image from a byte array.</summary>
    public static ImagePipeline Load(byte[] data, ImagingOptions? options = null) => Load(data.AsSpan(), options);

    /// <summary>Load an image from a stream.</summary>
    public static ImagePipeline Load(Stream stream, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        using var ms = new MemoryStream();
        CopyWithLimit(stream, ms, options.MaxInputBytes);
        return Load(ms.GetBuffer().AsSpan(0, (int)ms.Length), options);
    }

    /// <summary>Load an image asynchronously from a ReadOnlyMemory.</summary>
    public static Task<ImagePipeline> LoadAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default, ImagingOptions? options = null)
        => Task.Run(() => Load(data.Span, options), ct);

    /// <summary>Load an image asynchronously from a stream.</summary>
    public static async Task<ImagePipeline> LoadAsync(Stream stream, CancellationToken ct = default, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        using var ms = new MemoryStream();
        await CopyWithLimitAsync(stream, ms, options.MaxInputBytes, ct).ConfigureAwait(false);
        // Use GetBuffer()/Length to avoid a second allocation from ToArray().
        var buf = ms.GetBuffer();
        var len = (int)ms.Length;
        return await Task.Run(() => Load(buf.AsSpan(0, len), options), ct).ConfigureAwait(false);
    }

    private static void CopyWithLimit(Stream source, MemoryStream target, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new ImagingException($"Input stream exceeds size limit ({maxBytes} bytes).", ImagingError.InputTooLarge);
            target.Write(buffer, 0, read);
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, MemoryStream target, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new ImagingException($"Input stream exceeds size limit ({maxBytes} bytes).", ImagingError.InputTooLarge);
            target.Write(buffer, 0, read);
        }
    }
}
