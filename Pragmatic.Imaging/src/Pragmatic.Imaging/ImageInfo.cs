using System.Buffers;
using Pragmatic.Imaging.Engine;

namespace Pragmatic.Imaging;

/// <summary>
/// Image metadata obtained without full decode.
/// </summary>
public readonly record struct ImageInfo(uint Width, uint Height, ImageFormat Format)
{
    /// <summary>
    /// Read image dimensions and format from encoded bytes without full decode.
    /// </summary>
    /// <remarks>
    /// For a JPEG or PNG with an EXIF orientation that turns the image a quarter, width and height are
    /// the size it is shown at, the size <see cref="ImagePipeline.Load(ReadOnlySpan{byte}, ImagingOptions?)"/> produces.
    /// </remarks>
    public static ImageInfo FromBytes(ReadOnlySpan<byte> data) => ImageDecoding.Identify(data);

    /// <summary>
    /// Read image dimensions and format from a stream.
    /// Reads only the first <paramref name="maxBytes"/> bytes (default 64 KB — sufficient for any image header).
    /// Does NOT drain the entire stream.
    /// </summary>
    public static ImageInfo FromStream(Stream stream, long maxBytes = 65536)
    {
        var buffer = RentedStreamReader.Read(stream, maxBytes, overLimitThrows: false, out var length);
        try
        {
            return FromBytes(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Read image dimensions and format from a stream asynchronously.
    /// Reads only the first <paramref name="maxBytes"/> bytes (default 64 KB — sufficient for any image header).
    /// Does NOT drain the entire stream.
    /// </summary>
    public static async Task<ImageInfo> FromStreamAsync(Stream stream, long maxBytes = 65536, CancellationToken ct = default)
    {
        var (buffer, length) = await RentedStreamReader.ReadAsync(stream, maxBytes, overLimitThrows: false, ct).ConfigureAwait(false);
        try
        {
            // FromBytes reads a header and nothing more; no Task.Run overhead needed.
            return FromBytes(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
