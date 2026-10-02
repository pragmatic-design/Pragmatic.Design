using System.Runtime.InteropServices;
using Pragmatic.Imaging.Native;

namespace Pragmatic.Imaging;

/// <summary>
/// Fluent pipeline for image processing operations.
/// Thread-unsafe: do not share across threads. Always use with <c>using</c>.
/// <code>
/// using var result = ImagePipeline
///     .Load(imageBytes)
///     .Resize(800, 600)
///     .Grayscale()
///     .Encode(ImageFormat.WebP, quality: 80);
/// </code>
/// </summary>
public sealed partial class ImagePipeline : IDisposable, IAsyncDisposable
{
    private RawImage _image;
    private bool _disposed;

    private ImagePipeline(RawImage image) => _image = image;

    /// <summary>Current image width.</summary>
    public uint Width { get { ThrowIfDisposed(); return _image.Width; } }

    /// <summary>Current image height.</summary>
    public uint Height { get { ThrowIfDisposed(); return _image.Height; } }

    // --- Transform operations ---

    /// <summary>Resize the image to the specified dimensions.</summary>
    public ImagePipeline Resize(uint width, uint height, ResizeFilter filter = ResizeFilter.Lanczos3)
    {
        ThrowIfDisposed();
        if (width == 0 || height == 0)
            throw new ArgumentException("Resize dimensions must be greater than zero.");

        unsafe
        {
            nint outPtr;
            nuint outLen;
            var ptr = PinPixels(out var dataLen, out var pin);
            try
            {
                var rc = NativeImports.pragmatic_image_resize(
                    ptr, dataLen, _image.Width, _image.Height,
                    width, height, (int)filter, &outPtr, &outLen);
                NativeError.CheckResult(rc, "image_resize");
            }
            finally { ReleasePin(pin); }

            ReplaceImage(new NativeBuffer(outPtr, outLen), width, height);
        }

        return this;
    }

    /// <summary>
    /// Resize the image to fit within the given bounds while preserving aspect ratio.
    /// Never upscales: an image already smaller than the bounds is returned at its
    /// original size (unless <paramref name="allowUpscale"/> is true).
    /// </summary>
    public ImagePipeline Thumbnail(uint maxWidth, uint maxHeight, ResizeFilter filter = ResizeFilter.Lanczos3, bool allowUpscale = false)
    {
        ThrowIfDisposed();
        if (maxWidth == 0 || maxHeight == 0)
            throw new ArgumentException("Thumbnail dimensions must be greater than zero.");

        var ratioW = (double)maxWidth / Width;
        var ratioH = (double)maxHeight / Height;
        var ratio = Math.Min(ratioW, ratioH);

        // Clamp to no-upscale: "thumbnail" means a smaller (or same-size) preview, never larger.
        if (!allowUpscale)
            ratio = Math.Min(ratio, 1.0);

        var newW = (uint)Math.Max(1, Math.Round(Width * ratio));
        var newH = (uint)Math.Max(1, Math.Round(Height * ratio));

        return Resize(newW, newH, filter);
    }

    /// <summary>Crop a region from the image.</summary>
    public ImagePipeline Crop(uint x, uint y, uint width, uint height)
    {
        ThrowIfDisposed();
        if (width == 0 || height == 0)
            throw new ArgumentException("Crop dimensions must be greater than zero.");
        // Widen to ulong: `x + width` in uint arithmetic wraps silently for huge values,
        // letting an out-of-bounds region slip past the check.
        if ((ulong)x + width > Width || (ulong)y + height > Height)
            throw new ArgumentException($"Crop region ({x},{y},{width},{height}) exceeds image bounds ({Width}x{Height}).");

        unsafe
        {
            nint outPtr;
            nuint outLen;
            var ptr = PinPixels(out var dataLen, out var pin);
            try
            {
                var rc = NativeImports.pragmatic_image_crop(
                    ptr, dataLen, _image.Width, _image.Height,
                    x, y, width, height, &outPtr, &outLen);
                NativeError.CheckResult(rc, "image_crop");
            }
            finally { ReleasePin(pin); }

            ReplaceImage(new NativeBuffer(outPtr, outLen), width, height);
        }

        return this;
    }

    /// <summary>Rotate the image. Only 90, 180, and 270 degrees are supported.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="degrees"/> is not 90, 180, or 270.</exception>
    public ImagePipeline Rotate(int degrees)
    {
        ThrowIfDisposed();
        if (degrees is not (90 or 180 or 270))
            throw new ArgumentException("Only 90, 180, and 270 degrees are supported.", nameof(degrees));

        unsafe
        {
            nint outPtr;
            nuint outLen;
            uint newW, newH;
            var ptr = PinPixels(out var dataLen, out var pin);
            try
            {
                var rc = NativeImports.pragmatic_image_rotate(
                    ptr, dataLen, _image.Width, _image.Height,
                    degrees, &outPtr, &outLen, &newW, &newH);
                NativeError.CheckResult(rc, "image_rotate");
            }
            finally { ReleasePin(pin); }

            ReplaceImage(new NativeBuffer(outPtr, outLen), newW, newH);
        }

        return this;
    }

    // --- Filters ---

    /// <summary>Flip the image horizontally.</summary>
    public ImagePipeline FlipHorizontal() => ApplyFilter(FilterOp.FlipH);
    /// <summary>Flip the image vertically.</summary>
    public ImagePipeline FlipVertical() => ApplyFilter(FilterOp.FlipV);
    /// <summary>Convert to grayscale.</summary>
    public ImagePipeline Grayscale() => ApplyFilter(FilterOp.Grayscale);
    /// <summary>Apply Gaussian blur with the specified sigma.</summary>
    public ImagePipeline Blur(float sigma) => ApplyFilter(FilterOp.Blur, floatParam: sigma);
    /// <summary>Apply unsharpen mask.</summary>
    public ImagePipeline Sharpen(float sigma = 1.0f, int threshold = 1)
        => ApplyFilter(FilterOp.Sharpen, floatParam: sigma, intParam: threshold);
    /// <summary>Adjust brightness (-255 to 255).</summary>
    public ImagePipeline Brightness(int value) => ApplyFilter(FilterOp.Brightness, intParam: value);
    /// <summary>Adjust contrast (-100 to 100).</summary>
    public ImagePipeline Contrast(float value) => ApplyFilter(FilterOp.Contrast, floatParam: value);

    // --- Encode ---

    /// <summary>Encode the current image to the specified format.</summary>
    /// <param name="format">Target format.</param>
    /// <param name="quality">Quality for JPEG (1-100). WebP/AVIF use library defaults. Ignored for lossless formats.</param>
    public byte[] Encode(ImageFormat format = ImageFormat.Png, byte quality = 90)
    {
        ThrowIfDisposed();

        unsafe
        {
            nint outData;
            nuint outLen;
            var ptr = PinPixels(out var dataLen, out var pin);
            try
            {
                var rc = NativeImports.pragmatic_image_encode(
                    ptr, dataLen, _image.Width, _image.Height,
                    (int)format, quality, &outData, &outLen);
                NativeError.CheckResult(rc, "image_encode");
            }
            finally { ReleasePin(pin); }

            using var buffer = new NativeBuffer(outData, outLen);
            return buffer.ToArray();
        }
    }

    /// <summary>Encode and write to a stream.</summary>
    public void EncodeTo(Stream output, ImageFormat format = ImageFormat.Png, byte quality = 90)
        => output.Write(Encode(format, quality));

    /// <summary>Encode asynchronously.</summary>
    public Task<byte[]> EncodeAsync(ImageFormat format = ImageFormat.Png, byte quality = 90, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Encode(format, quality);
        }, ct);

    /// <summary>Encode and write to a stream asynchronously.</summary>
    public async Task EncodeToStreamAsync(Stream output, ImageFormat format = ImageFormat.Png, byte quality = 90, CancellationToken ct = default)
    {
        var bytes = await EncodeAsync(format, quality, ct).ConfigureAwait(false);
        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    // --- Internal ---

    private enum FilterOp { FlipH, FlipV, Grayscale, Blur, Sharpen, Brightness, Contrast }

    private unsafe ImagePipeline ApplyFilter(FilterOp op, float floatParam = 0, int intParam = 0)
    {
        ThrowIfDisposed();
        nint outPtr;
        nuint outLen;
        // Capture dimensions before the call; these filters are all dimension-preserving.
        // If this method is extended with resizing ops in the future, those ops must
        // return new dimensions from the native call and pass them to ReplaceImage explicitly.
        var srcW = _image.Width;
        var srcH = _image.Height;

        var ptr = PinPixels(out var dataLen, out var pin);
        try
        {
            var rc = op switch
            {
                FilterOp.FlipH => NativeImports.pragmatic_image_flip(ptr, dataLen, srcW, srcH, 1, &outPtr, &outLen),
                FilterOp.FlipV => NativeImports.pragmatic_image_flip(ptr, dataLen, srcW, srcH, 0, &outPtr, &outLen),
                FilterOp.Grayscale => NativeImports.pragmatic_image_grayscale(ptr, dataLen, srcW, srcH, &outPtr, &outLen),
                FilterOp.Blur => NativeImports.pragmatic_image_blur(ptr, dataLen, srcW, srcH, floatParam, &outPtr, &outLen),
                FilterOp.Sharpen => NativeImports.pragmatic_image_sharpen(ptr, dataLen, srcW, srcH, floatParam, intParam, &outPtr, &outLen),
                FilterOp.Brightness => NativeImports.pragmatic_image_brightness(ptr, dataLen, srcW, srcH, intParam, &outPtr, &outLen),
                FilterOp.Contrast => NativeImports.pragmatic_image_contrast(ptr, dataLen, srcW, srcH, floatParam, &outPtr, &outLen),
                _ => throw new ArgumentOutOfRangeException(nameof(op))
            };
            NativeError.CheckResult(rc, op.ToString());
        }
        finally { ReleasePin(pin); }

        // All filters above preserve dimensions; pass captured srcW/srcH.
        ReplaceImage(new NativeBuffer(outPtr, outLen), srcW, srcH);
        return this;
    }

    /// <summary>
    /// Pin pixel data for native call. Fast path: if backed by native buffer, returns pointer directly (no copy).
    /// Slow path: copies to managed array and pins via GCHandle.
    /// Caller MUST call <see cref="ReleasePin"/> in a finally block.
    /// </summary>
    private unsafe byte* PinPixels(out nuint length, out GCHandle pin)
    {
        if (_image.HasNativeBuffer)
        {
            pin = default;
            length = _image.NativeLength;
            return (byte*)_image.NativePtr;
        }

        var pixels = _image.ToArray();
        pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        length = (nuint)pixels.Length;
        return (byte*)pin.AddrOfPinnedObject();
    }

    private static void ReleasePin(GCHandle pin)
    {
        if (pin.IsAllocated) pin.Free();
    }

    private void ReplaceImage(NativeBuffer buffer, uint width, uint height)
    {
        var newImage = RawImage.FromNative(buffer, width, height);
        _image.Dispose();
        _image = newImage;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _image.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
