namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers <see cref="LimitedReadStream" />, the limiting wrapper used by the S3 provider
///     to enforce <c>MaxFileSizeBytes</c> on non-seekable upload streams — where the
///     length is unknown up front and the seekable fast-path cannot apply.
/// </summary>
public sealed class LimitedReadStreamTests
{
    private const long Limit = 16;

    /// <summary>A non-seekable wrapper, like an HTTP request body handed to S3 PutObject.</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ReadAsync_NonSeekableOverLimit_Throws()
    {
        // Mirrors how the S3 SDK drains the wrapped stream while uploading.
        using var limited = new LimitedReadStream(new NonSeekableStream(new byte[Limit + 8]), Limit);
        var buffer = new byte[4];

        var act = async () =>
        {
            int read;
            while ((read = await limited.ReadAsync(buffer).ConfigureAwait(false)) > 0) { _ = read; }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task ReadAsync_NonSeekableUnderLimit_DrainsFully()
    {
        using var limited = new LimitedReadStream(new NonSeekableStream(new byte[Limit - 1]), Limit);
        var buffer = new byte[4];

        long total = 0;
        int read;
        while ((read = await limited.ReadAsync(buffer).ConfigureAwait(true)) > 0)
            total += read;

        total.Should().Be(Limit - 1);
    }

    [Fact]
    public void Read_SyncOverLimit_Throws()
    {
        using var limited = new LimitedReadStream(new NonSeekableStream(new byte[Limit + 8]), Limit);
        var buffer = new byte[4];

        var act = () =>
        {
            int read;
            while ((read = limited.Read(buffer, 0, buffer.Length)) > 0) { _ = read; }
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*exceeds*");
    }
}
