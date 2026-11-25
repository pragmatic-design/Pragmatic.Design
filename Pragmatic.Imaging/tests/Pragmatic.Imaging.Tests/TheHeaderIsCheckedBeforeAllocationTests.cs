using System.Buffers.Binary;
using Pragmatic.Imaging.Codecs;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Imaging.Tests;

/// <summary>
///     A file that announces more pixels than the limit is refused from its header, before a pixel buffer
///     exists: a 50-byte upload claiming 20000×20000 does not get 1.6 GB allocated to find out it lied.
/// </summary>
/// <remarks>
///     <para>
///         Pixel memory comes from <see cref="NativeBufferPool" />, which the managed heap does not see, so
///         both are read: the bytes the pool ever rented (a count that only grows, so a buffer allocated
///         and freed again on the way to the exception still shows) and this thread's managed allocations.
///         Out of the parallel run, so no other test's image lands between two readings of the pool.
///     </para>
///     <para>
///         The bomb has an IDAT chunk, so a decode that started would get as far as allocating its
///         pixels before the deflate data ran out: without one, the decoder stops on the missing chunk
///         first and the test could not tell a refusal from that.
///     </para>
/// </remarks>
[Collection(nameof(TheHeaderIsCheckedBeforeAllocationTests))]
[CollectionDefinition(nameof(TheHeaderIsCheckedBeforeAllocationTests), DisableParallelization = true)]
public class TheHeaderIsCheckedBeforeAllocationTests
{
    private const int Side = 20_000;

    [Fact]
    public void A_png_announcing_too_many_pixels_is_refused_with_nothing_allocated()
    {
        var bomb = PngBomb(Side, Side);

        var poolBefore = NativeBufferPool.RentedBytes;
        var heapBefore = GC.GetAllocatedBytesForCurrentThread();
        var thrown = Record.Exception(() => ImagePipeline.Load(bomb));
        var rented = NativeBufferPool.RentedBytes - poolBefore;
        var managed = GC.GetAllocatedBytesForCurrentThread() - heapBefore;

        rented.Should().Be(0);
        // The exception and its message, a few kilobytes; a buffer would be 1.6 GB.
        managed.Should().BeLessThan(256 * 1024);
        thrown.Should().BeOfType<ImagingException>();
        ((ImagingException)thrown!).Reason.Should().Be(ImagingError.MaxMegapixelsExceeded);
    }

    [Fact]
    public void A_png_within_the_limit_does_allocate_its_pixels()
    {
        // The control: the same reading sees a decode, so a zero above is the refusal, not a blind spot.
        var png = TestHelper.CreateTestPng(64, 64);

        var poolBefore = NativeBufferPool.RentedBytes;
        using var pipeline = ImagePipeline.Load(png);

        (NativeBufferPool.RentedBytes - poolBefore).Should().Be(64 * 64 * 4);
    }

    /// <summary>
    ///     A PNG signature, an IHDR announcing <paramref name="width" /> × <paramref name="height" /> RGBA,
    ///     and an IDAT with two bytes of zlib header and nothing after them.
    /// </summary>
    private static byte[] PngBomb(int width, int height)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA

        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR"u8, ihdr), .. Chunk("IDAT"u8, [0x78, 0x9C])];
    }

    private static byte[] Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        type.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }
}
