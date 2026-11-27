using System.Buffers;

namespace Pragmatic.Imaging.Codecs.WebP;

/// <summary>
///     A decoded VP8 key frame as its three planes, each a whole number of macroblocks wide and high: Y at
///     full resolution, U and V at half. Only the top-left <see cref="Width" /> x <see cref="Height" /> is
///     the image. The planes are rented from the shared pool by <see cref="Rent" />; dispose the frame to give
///     them back. The object itself outlives a decode: the decoder keeps one per thread.
/// </summary>
internal sealed class Vp8Frame : IDisposable
{
    public int Width { get; private set; }

    public int Height { get; private set; }

    public int MacroblocksWide { get; private set; }

    public int MacroblocksHigh { get; private set; }

    public int YStride { get; private set; }

    public int UvStride { get; private set; }

    public byte[] Y { get; private set; } = [];

    public byte[] U { get; private set; } = [];

    public byte[] V { get; private set; } = [];

    /// <summary>Takes the planes of a <paramref name="width" /> x <paramref name="height" /> frame from the pool.</summary>
    public void Rent(int width, int height)
    {
        Width = width;
        Height = height;
        MacroblocksWide = (width + 15) >> 4;
        MacroblocksHigh = (height + 15) >> 4;
        YStride = MacroblocksWide * 16;
        UvStride = MacroblocksWide * 8;
        Y = ArrayPool<byte>.Shared.Rent(YStride * MacroblocksHigh * 16);
        U = ArrayPool<byte>.Shared.Rent(UvStride * MacroblocksHigh * 8);
        V = ArrayPool<byte>.Shared.Rent(UvStride * MacroblocksHigh * 8);
    }

    /// <summary>Gives the planes back; a second call, or one before <see cref="Rent" />, does nothing.</summary>
    public void Dispose()
    {
        if (Y.Length == 0)
            return;
        ArrayPool<byte>.Shared.Return(Y);
        ArrayPool<byte>.Shared.Return(U);
        ArrayPool<byte>.Shared.Return(V);
        Y = U = V = [];
    }
}
