using Xunit;

namespace Pragmatic.Imaging.Codecs.Tests;

public class NativeBufferPoolTests
{
    [Fact]
    public unsafe void A_disposed_buffer_is_handed_out_again_for_the_same_size()
    {
        NativeBufferPool.Clear();
        var first = new PixelBuffer(640, 480);
        var pointer = (nint)first.Pointer;
        first.Dispose();

        Assert.Equal(640 * 480 * 4, NativeBufferPool.RetainedBytes);

        using var second = new PixelBuffer(640, 480);
        Assert.Equal(pointer, (nint)second.Pointer);
        Assert.Equal(0, NativeBufferPool.RetainedBytes);
    }

    [Fact]
    public void A_different_size_gets_a_different_block()
    {
        NativeBufferPool.Clear();
        var small = new PixelBuffer(64, 64);
        small.Dispose();

        using var large = new PixelBuffer(128, 128);
        Assert.Equal(64 * 64 * 4, NativeBufferPool.RetainedBytes);
    }

    [Fact]
    public void Past_the_limit_a_returned_block_is_freed_not_kept()
    {
        NativeBufferPool.Clear();
        var limit = NativeBufferPool.RetainedLimitBytes;
        try
        {
            NativeBufferPool.RetainedLimitBytes = 100 * 100 * 4;
            new PixelBuffer(100, 100).Dispose();
            Assert.Equal(100 * 100 * 4, NativeBufferPool.RetainedBytes);

            new PixelBuffer(100, 100).Dispose();
            Assert.Equal(100 * 100 * 4, NativeBufferPool.RetainedBytes);
        }
        finally
        {
            NativeBufferPool.RetainedLimitBytes = limit;
            NativeBufferPool.Clear();
        }
    }

    [Fact]
    public void Using_a_disposed_buffer_throws()
    {
        var buffer = new PixelBuffer(8, 8);
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.Pixels.Length);
    }
}
