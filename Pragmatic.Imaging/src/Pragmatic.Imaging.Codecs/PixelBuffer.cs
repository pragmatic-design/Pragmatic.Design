namespace Pragmatic.Imaging.Codecs;

/// <summary>
///     An image as pixels of one <see cref="PixelFormat" />, 8-bit RGBA unless asked otherwise, row-major, no padding,
///     in memory the garbage collector does not manage: the same place today's native library keeps them, so a 4 MP
///     image is 16 MB that never sits on the large object heap. The memory comes from <see cref="NativeBufferPool" />
///     and goes back there on dispose, for the next image of the same size.
/// </summary>
/// <remarks>
///     Not thread-safe. Dispose it; a finalizer returns what a caller forgot, but late.
/// </remarks>
public sealed unsafe class PixelBuffer : IDisposable
{
    private byte* _pixels;

    public PixelBuffer(int width, int height, PixelFormat format = PixelFormat.Rgba8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var bytes = format.BytesPerPixel();
        var length = (long)width * height * bytes;
        if (length > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), $"An image of {width}x{height} needs {length} bytes, more than one span can address.");

        Width = width;
        Height = height;
        Format = format;
        BytesPerPixel = bytes;
        _pixels = NativeBufferPool.Rent((nuint)length);
    }

    public int Width { get; }

    public int Height { get; }

    public PixelFormat Format { get; }

    public int BytesPerPixel { get; }

    /// <summary>Bytes per row: <see cref="BytesPerPixel" /> per pixel, no padding.</summary>
    public int Stride => Width * BytesPerPixel;

    public int ByteLength => Width * Height * BytesPerPixel;

    /// <summary>Every pixel of the image, as one span.</summary>
    public Span<byte> Pixels
    {
        get
        {
            ThrowIfDisposed();
            return new Span<byte>(_pixels, ByteLength);
        }
    }

    /// <summary>The pixels of row <paramref name="y" />.</summary>
    public Span<byte> Row(int y)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return new Span<byte>(_pixels + (long)y * Stride, Stride);
    }

    /// <summary>The first byte, 64-byte aligned, for code that walks the pixels with pointers.</summary>
    public byte* Pointer
    {
        get
        {
            ThrowIfDisposed();
            return _pixels;
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~PixelBuffer() => Release();

    private void Release()
    {
        if (_pixels is null)
            return;
        NativeBufferPool.Return(_pixels, (nuint)ByteLength);
        _pixels = null;
    }

    private void ThrowIfDisposed()
    {
        if (_pixels is null)
            throw new ObjectDisposedException(nameof(PixelBuffer));
    }
}
