using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs;

/// <summary>
///     Keeps freed native buffers for the next image of the same size. A fresh 16 MB block costs the
///     operating system a page fault per 4 KB page on first touch, about 1.5 ms per 4 MP image; a block
///     from here costs nothing. Bounded: past <see cref="RetainedLimitBytes" /> a returned block is freed,
///     so a burst of large images does not pin their memory for the life of the process.
/// </summary>
public static unsafe class NativeBufferPool
{
    private const int Alignment = 64;

    private static readonly ConcurrentDictionary<nuint, ConcurrentBag<nint>> Blocks = new();
    private static long _retainedBytes;

    /// <summary>How much the pool may hold at rest. Default 128 MB: eight 4 MP RGBA images.</summary>
    public static long RetainedLimitBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>Bytes held at rest right now.</summary>
    public static long RetainedBytes => Interlocked.Read(ref _retainedBytes);

    /// <summary>A 64-byte-aligned block of exactly <paramref name="length" /> bytes, contents undefined.</summary>
    public static byte* Rent(nuint length)
    {
        if (Blocks.TryGetValue(length, out var bag) && bag.TryTake(out var pointer))
        {
            Interlocked.Add(ref _retainedBytes, -(long)length);
            return (byte*)pointer;
        }

        return (byte*)NativeMemory.AlignedAlloc(length, Alignment);
    }

    /// <summary>Gives a block back: kept for reuse while under the limit, freed otherwise.</summary>
    public static void Return(byte* pointer, nuint length)
    {
        if (Interlocked.Read(ref _retainedBytes) + (long)length > RetainedLimitBytes)
        {
            NativeMemory.AlignedFree(pointer);
            return;
        }

        Interlocked.Add(ref _retainedBytes, (long)length);
        Blocks.GetOrAdd(length, static _ => []).Add((nint)pointer);
    }

    /// <summary>Frees everything held at rest.</summary>
    public static void Clear()
    {
        foreach (var (length, bag) in Blocks)
        {
            while (bag.TryTake(out var pointer))
            {
                NativeMemory.AlignedFree((void*)pointer);
                Interlocked.Add(ref _retainedBytes, -(long)length);
            }
        }
    }
}
