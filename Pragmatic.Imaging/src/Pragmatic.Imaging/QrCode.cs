using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Png;
using CoreQr = Pragmatic.Imaging.Codecs.Barcodes.QrCode;

namespace Pragmatic.Imaging;

/// <summary>
/// QR code generation utilities.
/// </summary>
/// <remarks>
/// The symbol is the managed core's: byte mode (UTF-8), error correction level M, the smallest version
/// that holds the text, the mask with the lowest penalty. Drawn black on white, with the quiet zone.
/// </remarks>
public static class QrCode
{
    /// <summary>
    /// The largest side a rendered QR may have: an 8192×8192 RGBA image is 256 MB, and no legitimate QR
    /// needs more (a version-40 symbol, 177 modules, fits at module size 40 with its margin).
    /// </summary>
    private const long MaxDimension = 8192;

    /// <summary>
    /// Generate a QR code as PNG bytes.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="moduleSize">Pixel size of each QR module (default 10).</param>
    /// <param name="margin">Quiet zone in modules (default 2).</param>
    /// <returns>PNG-encoded QR code image.</returns>
    /// <exception cref="ArgumentException">The text is empty or does not fit a QR code, or a size is out of range.</exception>
    /// <exception cref="ImagingException">The rendered image would exceed 8192 pixels a side.</exception>
    public static byte[] GeneratePng(string text, uint moduleSize = 10, uint margin = 2)
    {
        using var output = new MemoryStream();
        GeneratePng(text, output, moduleSize, margin);
        return output.ToArray();
    }

    /// <summary>
    /// Generate a QR code and write it to a stream.
    /// </summary>
    /// <remarks>The PNG is written straight into <paramref name="output"/>.</remarks>
    public static void GeneratePng(string text, Stream output, uint moduleSize = 10, uint margin = 2)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(output);
        if (text.Length == 0)
            throw new ArgumentException("Text must not be empty.", nameof(text));
        if (moduleSize == 0 || moduleSize > 1000)
            throw new ArgumentException("moduleSize must be between 1 and 1000.", nameof(moduleSize));
        if (margin > 100)
            throw new ArgumentException("margin must be at most 100.", nameof(margin));

        var symbol = CoreQr.Encode(text);
        // In longs: moduleSize and margin are each bounded, their products are not bounded by uint.
        var side = ((long)symbol.Size + 2L * margin) * moduleSize;
        if (side > MaxDimension)
            throw new ImagingException(
                $"QR image dimension ({side}px) exceeds limit ({MaxDimension}px); reduce moduleSize or margin.");

        using var image = Render(symbol, (int)side, (int)moduleSize, (int)margin);
        PngEncoder.Write(image, output, Engine.ImageEncoding.PngCompression);
    }

    /// <summary>
    /// Generate a QR code as PNG bytes asynchronously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The synchronous <see cref="GeneratePng(string, uint, uint)"/> call is CPU-bound and has no
    /// overlapped/async path. To prevent it from stalling the calling thread (notably an ASP.NET Core
    /// request thread), this overload offloads the work onto a <see cref="System.Threading.ThreadPool"/>
    /// worker via <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Because <c>Task.Run</c> dispatches to the ThreadPool, the caller's <see cref="System.Threading.SynchronizationContext"/>
    /// is intentionally not captured; the continuation resumes on a ThreadPool thread (the standard pattern
    /// for CPU-bound work on ASP.NET Core).
    /// </para>
    /// <para>
    /// Use is intended for one-off generations. Callers in tight loops should batch the work themselves
    /// (e.g. produce a list of payloads and process them with bounded parallelism) rather than awaiting
    /// each call individually, to amortise the ThreadPool dispatch cost.
    /// </para>
    /// </remarks>
    public static Task<byte[]> GeneratePngAsync(string text, uint moduleSize = 10, uint margin = 2, CancellationToken ct = default)
        => Task.Run(() => GeneratePng(text, moduleSize, margin), ct);

    /// <summary>
    /// Generate a QR code and write to a stream asynchronously.
    /// </summary>
    public static async Task GeneratePngAsync(string text, Stream output, uint moduleSize = 10, uint margin = 2, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var png = await GeneratePngAsync(text, moduleSize, margin, ct).ConfigureAwait(false);
        await output.WriteAsync(png, ct).ConfigureAwait(false);
    }

    /// <summary>Black modules on white, <paramref name="moduleSize"/> pixels each, after <paramref name="margin"/> modules of white.</summary>
    private static PixelBuffer Render(CoreQr symbol, int side, int moduleSize, int margin)
    {
        var image = new PixelBuffer(side, side);
        image.Pixels.Fill(255);
        var offset = margin * moduleSize;
        for (var my = 0; my < symbol.Size; my++)
        {
            var top = offset + my * moduleSize;
            var first = image.Row(top);
            for (var mx = 0; mx < symbol.Size; mx++)
            {
                if (!symbol[mx, my])
                    continue;
                var left = (offset + mx * moduleSize) * 4;
                for (var px = 0; px < moduleSize; px++)
                {
                    var at = left + px * 4;
                    first[at] = first[at + 1] = first[at + 2] = 0; // alpha stays 255
                }
            }

            // The module row's other pixel rows are copies of its first.
            for (var dy = 1; dy < moduleSize; dy++)
                first.CopyTo(image.Row(top + dy));
        }

        return image;
    }
}
