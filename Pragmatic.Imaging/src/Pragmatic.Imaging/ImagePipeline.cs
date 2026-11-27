using Pragmatic.Imaging.Codecs;
using Pragmatic.Imaging.Codecs.Transforms;
using Pragmatic.Imaging.Engine;

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
/// <remarks>
/// Every operation runs managed, on pixels in native memory that the garbage collector does not move or
/// count. PNG and JPEG are decoded and encoded managed too; the other formats go through the native
/// library at load and encode, and nowhere else.
/// </remarks>
public sealed partial class ImagePipeline : IDisposable, IAsyncDisposable
{
    private PixelBuffer _image;
    private bool _disposed;

    private ImagePipeline(PixelBuffer image) => _image = image;

    /// <summary>Current image width.</summary>
    public uint Width { get { ThrowIfDisposed(); return (uint)_image.Width; } }

    /// <summary>Current image height.</summary>
    public uint Height { get { ThrowIfDisposed(); return (uint)_image.Height; } }

    // --- Transform operations ---

    /// <summary>Resize the image to the specified dimensions.</summary>
    public ImagePipeline Resize(uint width, uint height, ResizeFilter filter = ResizeFilter.Lanczos3)
    {
        ThrowIfDisposed();
        if (width == 0 || height == 0)
            throw new ArgumentException("Resize dimensions must be greater than zero.");
        if (!Enum.IsDefined(filter))
            throw new ArgumentOutOfRangeException(nameof(filter), filter, "Not a resize filter.");

        Replace(Resampler.Resize(_image, Dimension(width), Dimension(height), (Codecs.Transforms.ResizeFilter)(int)filter));
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

        Replace(PixelGeometry.Crop(_image, (int)x, (int)y, (int)width, (int)height));
        return this;
    }

    /// <summary>Rotate the image clockwise. Only 90, 180, and 270 degrees are supported.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="degrees"/> is not 90, 180, or 270.</exception>
    public ImagePipeline Rotate(int degrees)
    {
        ThrowIfDisposed();
        Replace(degrees switch
        {
            90 => PixelGeometry.Rotate90(_image),
            180 => PixelGeometry.Rotate180(_image),
            270 => PixelGeometry.Rotate270(_image),
            _ => throw new ArgumentException("Only 90, 180, and 270 degrees are supported.", nameof(degrees)),
        });
        return this;
    }

    // --- Filters ---

    /// <summary>Flip the image horizontally.</summary>
    public ImagePipeline FlipHorizontal()
    {
        ThrowIfDisposed();
        PixelGeometry.FlipHorizontal(_image);
        return this;
    }

    /// <summary>Flip the image vertically.</summary>
    public ImagePipeline FlipVertical()
    {
        ThrowIfDisposed();
        PixelGeometry.FlipVertical(_image);
        return this;
    }

    /// <summary>Convert to grayscale (ITU-R BT.601 luma; alpha is kept).</summary>
    public ImagePipeline Grayscale()
    {
        ThrowIfDisposed();
        PixelAdjustments.Grayscale(_image);
        return this;
    }

    /// <summary>Apply a Gaussian blur with the specified sigma. Zero means the default of 0.8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sigma"/> is negative, NaN or infinite.</exception>
    public ImagePipeline Blur(float sigma)
    {
        ThrowIfDisposed();
        Replace(GaussianBlur.Blur(_image, sigma));
        return this;
    }

    /// <summary>Apply an unsharp mask.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sigma"/> is not a positive, finite number.</exception>
    public ImagePipeline Sharpen(float sigma = 1.0f, int threshold = 1)
    {
        ThrowIfDisposed();
        Replace(GaussianBlur.Unsharpen(_image, sigma, threshold));
        return this;
    }

    /// <summary>Adjust brightness (-255 to 255).</summary>
    public ImagePipeline Brightness(int value)
    {
        ThrowIfDisposed();
        PixelAdjustments.Brightness(_image, value);
        return this;
    }

    /// <summary>Adjust contrast (-100 to 100).</summary>
    public ImagePipeline Contrast(float value)
    {
        ThrowIfDisposed();
        PixelAdjustments.Contrast(_image, value);
        return this;
    }

    // --- Encode ---

    /// <summary>Encode the current image to the specified format.</summary>
    /// <param name="format">Target format.</param>
    /// <param name="quality">Quality for JPEG (1-100). WebP is written lossless and AVIF at the library default. Ignored for lossless formats.</param>
    public byte[] Encode(ImageFormat format = ImageFormat.Png, byte quality = 90)
    {
        ThrowIfDisposed();
        return ImageEncoding.Encode(_image, format, quality);
    }

    /// <summary>Encode and write to a stream.</summary>
    /// <remarks>PNG and JPEG are written straight into <paramref name="output"/>, without a copy of the encoded file.</remarks>
    public void EncodeTo(Stream output, ImageFormat format = ImageFormat.Png, byte quality = 90)
    {
        ArgumentNullException.ThrowIfNull(output);
        ThrowIfDisposed();
        ImageEncoding.Encode(_image, format, quality, output);
    }

    /// <summary>Encode the current image as WebP, lossless or lossy.</summary>
    /// <param name="encoding">Lossless, as <see cref="Encode" /> with <see cref="ImageFormat.WebP" /> writes, or lossy.</param>
    /// <param name="quality">For lossy, 0 to 100 on libwebp's scale, where 75 is its default. Ignored for lossless.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality" /> is above 100, or <paramref name="encoding" /> is not one of the values.</exception>
    public byte[] EncodeWebP(WebPEncoding encoding, byte quality = 75)
    {
        ThrowIfDisposed();
        return ImageEncoding.EncodeWebP(_image, encoding, quality);
    }

    /// <summary>Encode the current image as WebP, lossless or lossy, and write it to a stream.</summary>
    /// <param name="output">Where the file is written.</param>
    /// <param name="encoding">Lossless, as <see cref="EncodeTo" /> with <see cref="ImageFormat.WebP" /> writes, or lossy.</param>
    /// <param name="quality">For lossy, 0 to 100 on libwebp's scale, where 75 is its default. Ignored for lossless.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality" /> is above 100, or <paramref name="encoding" /> is not one of the values.</exception>
    public void EncodeWebPTo(Stream output, WebPEncoding encoding, byte quality = 75)
    {
        ArgumentNullException.ThrowIfNull(output);
        ThrowIfDisposed();
        ImageEncoding.EncodeWebP(_image, encoding, quality, output);
    }

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

    private static int Dimension(uint value)
        => value <= int.MaxValue ? (int)value : throw new ArgumentOutOfRangeException(nameof(value), value, "A dimension above int.MaxValue cannot be allocated.");

    private void Replace(PixelBuffer image)
    {
        if (ReferenceEquals(image, _image))
            return;
        _image.Dispose();
        _image = image;
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
