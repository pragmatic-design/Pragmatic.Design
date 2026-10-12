using System.Buffers;
using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging;

/// <summary>
/// Image metadata obtained without full decode.
/// </summary>
public readonly record struct ImageInfo(uint Width, uint Height, ImageFormat Format)
{
    /// <summary>
    /// Read image dimensions and format from encoded bytes without full decode.
    /// </summary>
    public static ImageInfo FromBytes(ReadOnlySpan<byte> data)
    {
        unsafe
        {
            uint w, h;
            int fmt;
            fixed (byte* ptr = data)
            {
                var rc = NativeImports.pragmatic_image_info(ptr, (nuint)data.Length, &w, &h, &fmt);
                NativeError.CheckResult(rc, "image_info");
            }
            return new ImageInfo(w, h, (ImageFormat)fmt);
        }
    }

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
            // FromBytes is a fast native header-read; no Task.Run overhead needed.
            return FromBytes(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
