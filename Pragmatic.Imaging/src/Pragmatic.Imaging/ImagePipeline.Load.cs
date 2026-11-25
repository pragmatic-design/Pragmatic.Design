using System.Buffers;
using Pragmatic.Imaging.Engine;

namespace Pragmatic.Imaging;

public sealed partial class ImagePipeline
{
    /// <summary>Load an image from encoded bytes (any supported format).</summary>
    /// <param name="data">Encoded image bytes.</param>
    /// <param name="options">Safety limits. Defaults to <see cref="ImagingOptions.Default"/>.</param>
    /// <remarks>
    /// A JPEG or PNG with an EXIF orientation is loaded the way it is shown: a portrait phone photo
    /// stored sideways comes out upright, and <see cref="Width"/> and <see cref="Height"/> are the shown size.
    /// </remarks>
    public static ImagePipeline Load(ReadOnlySpan<byte> data, ImagingOptions? options = null)
    {
        unsafe
        {
            fixed (byte* pointer = data)
            {
                using var memory = new PinnedMemory(pointer, data.Length);
                return Load(memory.Memory, options);
            }
        }
    }

    /// <summary>Load an image from a byte array.</summary>
    public static ImagePipeline Load(byte[] data, ImagingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Load((ReadOnlyMemory<byte>)data, options);
    }

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
            return Load(buffer.AsMemory(0, length), options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Load an image asynchronously from a ReadOnlyMemory.</summary>
    public static Task<ImagePipeline> LoadAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default, ImagingOptions? options = null)
        => Task.Run(() => Load(data, options), ct);

    /// <summary>Load an image asynchronously from a stream.</summary>
    /// <remarks>Reads into a rented buffer, as <see cref="Load(Stream, ImagingOptions?)" /> does.</remarks>
    public static async Task<ImagePipeline> LoadAsync(Stream stream, CancellationToken ct = default, ImagingOptions? options = null)
    {
        options ??= ImagingOptions.Default;
        var (buffer, length) = await RentedStreamReader.ReadAsync(stream, options.MaxInputBytes, overLimitThrows: true, ct)
            .ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Load(buffer.AsMemory(0, length), options), ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// The one path every overload takes: the input size, then the header (the shown size and the
    /// format), then the limits, and only then the pixels, so a file that lies about its size in its
    /// header is refused before anything is allocated for it.
    /// </summary>
    private static ImagePipeline Load(ReadOnlyMemory<byte> data, ImagingOptions? options)
    {
        options ??= ImagingOptions.Default;
        options.ValidateInput(data.Length);

        var info = ImageDecoding.Identify(data.Span);
        options.ValidateDecoded(info.Width, info.Height);
        options.ValidateFormat(info.Format);

        return new ImagePipeline(ImageDecoding.Decode(data, info.Format));
    }
}
