using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates the <c>maxFileSizeBytes</c> guard of <see cref="LocalDiskFileStorage"/>:
/// uploads that exceed the configured limit are rejected. The seekable fast-path checks
/// <c>Stream.Length</c> up front; non-seekable streams are checked mid-copy.
/// </summary>
public static class MaxFileSizeSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- maxFileSizeBytes enforcement (LocalDiskFileStorage) ---");

        var demoRoot = SampleSupport.CreateTempRoot();
        try
        {
            // Cap uploads at 16 bytes.
            const long limit = 16;
            var storage = new LocalDiskFileStorage(demoRoot, NullLogger<LocalDiskFileStorage>.Instance, maxFileSizeBytes: limit);
            Console.WriteLine($"Configured limit: {limit} bytes");

            // 1. A small file is accepted.
            var small = "tiny"u8.ToArray();
            using (var input = new MemoryStream(small))
            {
                var uri = await storage.SaveAsync(input, "small.txt", "uploads");
                Console.WriteLine($"Saved {small.Length}-byte file: {uri}");
            }

            // 2. An oversized SEEKABLE stream is rejected up front via the Length fast-path.
            var big = new byte[limit + 64];
            Array.Fill(big, (byte)'x');
            using (var seekable = new MemoryStream(big))
            {
                try
                {
                    await storage.SaveAsync(seekable, "big.txt", "uploads");
                    Console.WriteLine("  Saved oversized seekable file (UNEXPECTED).");
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"  Rejected oversized seekable file: {ex.Message}");
                }
            }

            // 3. An oversized NON-SEEKABLE stream is rejected mid-copy by CopyWithLimitAsync.
            using (var nonSeekable = new NonSeekableStream(big))
            {
                try
                {
                    await storage.SaveAsync(nonSeekable, "big-stream.txt", "uploads");
                    Console.WriteLine("  Saved oversized non-seekable file (UNEXPECTED).");
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"  Rejected oversized non-seekable file: {ex.Message}");
                }
            }
        }
        finally
        {
            SampleSupport.Cleanup(demoRoot);
        }

        Console.WriteLine();
    }

    /// <summary>A read-only stream wrapper that reports CanSeek = false, to exercise the mid-copy limit.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
