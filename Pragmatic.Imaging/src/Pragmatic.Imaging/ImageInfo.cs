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
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while (total < maxBytes && (read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - total))) > 0)
        {
            ms.Write(buffer, 0, read);
            total += read;
        }
        // Use GetBuffer() to avoid a second allocation — slice to actual written length.
        return FromBytes(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <summary>
    /// Read image dimensions and format from a stream asynchronously.
    /// Reads only the first <paramref name="maxBytes"/> bytes (default 64 KB — sufficient for any image header).
    /// Does NOT drain the entire stream.
    /// </summary>
    public static async Task<ImageInfo> FromStreamAsync(Stream stream, long maxBytes = 65536, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - total)), ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, read);
            total += read;
        }
        // FromBytes is a fast native header-read; no Task.Run overhead needed.
        return FromBytes(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }
}
