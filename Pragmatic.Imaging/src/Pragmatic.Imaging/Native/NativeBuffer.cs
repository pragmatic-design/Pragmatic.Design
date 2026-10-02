using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Native;

/// <summary>
/// Wraps a Rust-allocated buffer, ensuring proper deallocation via <c>pragmatic_free_buffer</c>.
/// </summary>
internal sealed class NativeBuffer : IDisposable
{
    private nint _ptr;
    private nuint _len;
    private bool _disposed;

    internal NativeBuffer(nint ptr, nuint len)
    {
        _ptr = ptr;
        _len = len;
    }

    ~NativeBuffer() => Dispose(false);

    /// <summary>The pointer to the native memory.</summary>
    internal nint Ptr => _ptr;

    /// <summary>The length in bytes.</summary>
    internal nuint Length => _len;

    /// <summary>Copy the native buffer contents into a managed byte array.</summary>
    internal byte[] ToArray()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ptr == 0 || _len == 0)
            return [];

        if (_len > int.MaxValue)
            throw new InvalidOperationException($"Buffer length {_len} exceeds int.MaxValue; cannot copy to managed array.");

        var result = new byte[(int)_len];
        Marshal.Copy(_ptr, result, 0, (int)_len);
        return result;
    }

    /// <summary>Copy the native buffer contents into a span.</summary>
    internal unsafe ReadOnlySpan<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ptr == 0 || _len == 0)
            return ReadOnlySpan<byte>.Empty;

        if (_len > int.MaxValue)
            throw new InvalidOperationException($"Buffer length {_len} exceeds int.MaxValue; cannot create Span.");

        return new ReadOnlySpan<byte>((void*)_ptr, (int)_len);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (_ptr != 0 && _len > 0)
        {
            NativeImports.pragmatic_free_buffer(_ptr, _len);
            _ptr = 0;
            _len = 0;
        }
    }
}
