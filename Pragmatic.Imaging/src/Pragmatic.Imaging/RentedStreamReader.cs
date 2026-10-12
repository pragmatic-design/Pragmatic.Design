using System.Buffers;

namespace Pragmatic.Imaging;

/// <summary>
///     Reads a stream into a buffer rented from <see cref="ArrayPool{T}.Shared" />, so the encoded
///     image is not copied into a fresh managed array (or a <see cref="MemoryStream" /> that doubles
///     as it grows) on every call. The caller returns the buffer to the pool.
/// </summary>
/// <remarks>
///     The first buffer is sized from what is left of a seekable stream, plus one byte so the read
///     that finds the end needs no growth; 80 KB when the length is unknown. It doubles as needed up
///     to the limit.
/// </remarks>
internal static class RentedStreamReader
{
    private const int UnknownLengthInitialSize = 81920;

    /// <summary>
    ///     Reads to the end of <paramref name="stream" />, or to <paramref name="limit" /> bytes. Past
    ///     the limit, <paramref name="overLimitThrows" /> decides: an <see cref="ImagingException" />,
    ///     or the first <paramref name="limit" /> bytes.
    /// </summary>
    internal static byte[] Read(Stream stream, long limit, bool overLimitThrows, out int length)
    {
        var cap = Cap(limit, overLimitThrows);
        var buffer = RentFirst(stream, limit, cap, overLimitThrows);
        length = 0;
        try
        {
            int read;
            while (length < cap && (read = stream.Read(buffer, length, Room(buffer, length, cap))) > 0)
            {
                length += read;
                if (length == buffer.Length && length < cap)
                    buffer = Grow(buffer, cap);
            }

            ThrowIfOverLimit(length, limit, overLimitThrows);
            return buffer;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <inheritdoc cref="Read" />
    internal static async Task<(byte[] Buffer, int Length)> ReadAsync(
        Stream stream, long limit, bool overLimitThrows, CancellationToken ct)
    {
        var cap = Cap(limit, overLimitThrows);
        var buffer = RentFirst(stream, limit, cap, overLimitThrows);
        var length = 0;
        try
        {
            int read;
            while (length < cap
                   && (read = await stream.ReadAsync(buffer.AsMemory(length, Room(buffer, length, cap)), ct).ConfigureAwait(false)) > 0)
            {
                length += read;
                if (length == buffer.Length && length < cap)
                    buffer = Grow(buffer, cap);
            }

            ThrowIfOverLimit(length, limit, overLimitThrows);
            return (buffer, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     How many bytes are worth reading: one past the limit when exceeding it is an error, since
    ///     that byte is what tells an over-limit input from one exactly at the limit.
    /// </summary>
    private static int Cap(long limit, bool overLimitThrows)
        => (int)Math.Clamp(overLimitThrows ? limit + 1 : limit, 0, Array.MaxLength);

    private static byte[] RentFirst(Stream stream, long limit, int cap, bool overLimitThrows)
    {
        if (!stream.CanSeek)
            return ArrayPool<byte>.Shared.Rent(Math.Min(UnknownLengthInitialSize, cap));

        var remaining = Math.Max(stream.Length - stream.Position, 0);
        if (overLimitThrows && remaining > limit)
            throw InputTooLarge(limit);

        return ArrayPool<byte>.Shared.Rent((int)Math.Min(remaining + 1, cap));
    }

    private static int Room(byte[] buffer, int length, int cap) => Math.Min(buffer.Length, cap) - length;

    private static byte[] Grow(byte[] full, int cap)
    {
        var grown = ArrayPool<byte>.Shared.Rent((int)Math.Min((long)full.Length * 2, cap));
        full.AsSpan().CopyTo(grown);
        ArrayPool<byte>.Shared.Return(full);
        return grown;
    }

    private static void ThrowIfOverLimit(int length, long limit, bool overLimitThrows)
    {
        if (overLimitThrows && length > limit)
            throw InputTooLarge(limit);
    }

    private static ImagingException InputTooLarge(long limit)
        => new($"Input stream exceeds size limit ({limit} bytes).", ImagingError.InputTooLarge);
}
