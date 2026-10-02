using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging;

/// <summary>
/// QR code generation utilities.
/// </summary>
public static class QrCode
{
    /// <summary>
    /// Generate a QR code as PNG bytes.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="moduleSize">Pixel size of each QR module (default 10).</param>
    /// <param name="margin">Quiet zone in modules (default 2).</param>
    /// <returns>PNG-encoded QR code image.</returns>
    public static byte[] GeneratePng(string text, uint moduleSize = 10, uint margin = 2)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            throw new ArgumentException("Text must not be empty.", nameof(text));
        if (moduleSize == 0 || moduleSize > 1000)
            throw new ArgumentException("moduleSize must be between 1 and 1000.", nameof(moduleSize));
        if (margin > 100)
            throw new ArgumentException("margin must be at most 100.", nameof(margin));

        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);

        unsafe
        {
            nint outPng;
            nuint outLen;

            fixed (byte* textPtr = textBytes)
            {
                var rc = NativeImports.pragmatic_qr_generate(
                    textPtr, (nuint)textBytes.Length,
                    moduleSize, margin,
                    &outPng, &outLen);
                NativeError.CheckResult(rc, "qr_generate");
            }

            using var buffer = new NativeBuffer(outPng, outLen);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// Generate a QR code and write it to a stream.
    /// </summary>
    public static void GeneratePng(string text, Stream output, uint moduleSize = 10, uint margin = 2)
    {
        ArgumentNullException.ThrowIfNull(output);
        var png = GeneratePng(text, moduleSize, margin);
        output.Write(png, 0, png.Length);
    }

    /// <summary>
    /// Generate a QR code as PNG bytes asynchronously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The synchronous <see cref="GeneratePng(string, uint, uint)"/> call invokes a native (Rust) P/Invoke
    /// that is fully CPU-bound and blocking — it has no overlapped/async path. To prevent it from stalling
    /// the calling thread (notably an ASP.NET Core request thread), this overload offloads the work onto a
    /// <see cref="System.Threading.ThreadPool"/> worker via <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/>.
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
}
