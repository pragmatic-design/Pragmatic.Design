using System.Buffers;
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
    /// <remarks>
    ///     The encoded bytes are read into a buffer rented from <see cref="ArrayPool{T}.Shared" /> and
    ///     returned once decoded, sized from the stream's remaining length when it can seek.
    /// </remarks>
    public static ImagePipeline Load(Stream stream, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        var buffer = RentedStreamReader.Read(stream, options.MaxInputBytes, overLimitThrows: true, out var length);
        try
        {
            return Load(buffer.AsSpan(0, length), options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Load an image asynchronously from a ReadOnlyMemory.</summary>
    public static Task<ImagePipeline> LoadAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default, ImagingOptions? options = null)
        => Task.Run(() => Load(data.Span, options), ct);

    /// <summary>Load an image asynchronously from a stream.</summary>
    /// <remarks>Reads into a rented buffer, as <see cref="Load(Stream, ImagingOptions?)" /> does.</remarks>
    public static async Task<ImagePipeline> LoadAsync(Stream stream, CancellationToken ct = default, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        var (buffer, length) = await RentedStreamReader.ReadAsync(stream, options.MaxInputBytes, overLimitThrows: true, ct)
            .ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(buffer.AsSpan(0, length), options), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
