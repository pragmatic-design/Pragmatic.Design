namespace Pragmatic.Imaging;

/// <summary>
/// Represents decoded RGBA8 pixel data with dimensions.
/// This is the in-memory representation used between pipeline steps.
/// </summary>
internal sealed class RawImage : IDisposable
{
    private byte[]? _pixels;
    private Native.NativeBuffer? _nativeBuffer;
    private bool _disposed;

    internal uint Width { get; }
    internal uint Height { get; }

    /// <summary>RGBA8 pixel data (4 bytes per pixel).</summary>
    internal ReadOnlySpan<byte> Pixels
    {
        get
        {
            if (_nativeBuffer is not null) return _nativeBuffer.AsSpan();
            if (_pixels is not null) return _pixels;
            return ReadOnlySpan<byte>.Empty;
        }
    }

    /// <summary>Total byte length of pixel data.</summary>
    internal long Length => (long)Width * Height * 4;

    private RawImage(uint width, uint height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Create from a managed byte array (takes ownership).</summary>
    internal static RawImage FromManaged(byte[] pixels, uint width, uint height)
        => new(width, height) { _pixels = pixels };

    /// <summary>Create from a native buffer (takes ownership of the buffer).</summary>
    internal static RawImage FromNative(Native.NativeBuffer buffer, uint width, uint height)
        => new(width, height) { _nativeBuffer = buffer };

    /// <summary>True if backed by a native buffer (avoids managed copy in pipeline).</summary>
    internal bool HasNativeBuffer => _nativeBuffer is not null;

    /// <summary>Native pointer (only valid when <see cref="HasNativeBuffer"/> is true).</summary>
    internal nint NativePtr => _nativeBuffer?.Ptr ?? 0;

    /// <summary>Native buffer length (only valid when <see cref="HasNativeBuffer"/> is true).</summary>
    internal nuint NativeLength => _nativeBuffer?.Length ?? 0;

    /// <summary>Get pixels as a pinnable array (copies from native if needed, memoized).</summary>
    internal byte[] ToArray()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pixels is not null) return _pixels;
        if (_nativeBuffer is not null)
        {
            // Memoize: copy once from native, reuse for subsequent pipeline steps
            _pixels = _nativeBuffer.ToArray();
            return _pixels;
        }
        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _nativeBuffer?.Dispose();
        _nativeBuffer = null;
        _pixels = null;
    }
}
