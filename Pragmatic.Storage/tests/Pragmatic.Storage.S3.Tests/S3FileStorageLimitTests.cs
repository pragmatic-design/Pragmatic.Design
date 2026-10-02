using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.S3.Tests;

/// <summary>
///     Covers the <c>MaxFileSizeBytes</c> enforcement of <see cref="S3FileStorage" /> on both the
///     seekable fast-path (rejected before any SDK call) and the streaming (non-seekable) path
///     via <see cref="LimitedReadStream" />.
/// </summary>
public sealed class S3FileStorageLimitTests
{
    private const long Limit = 16;
    private readonly AmazonS3Mock _s3 = new AmazonS3Mock();

    private S3FileStorage CreateLimitedStorage()
        => new(_s3, new S3StorageOptions { BucketName = "bkt", MaxFileSizeBytes = Limit },
            NullLogger<S3FileStorage>.Instance);

    /// <summary>Configures the mock to fully consume the request stream, like the real SDK.</summary>
    private void PutConsumesStream()
        => _s3.PutObjectAsync.Returns((request, _) =>
        {
            request.InputStream.CopyTo(Stream.Null);
            return Task.FromResult(new PutObjectResponse());
        });

    /// <summary>A non-seekable wrapper so the fast-path size check is bypassed.</summary>
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
    public async Task SaveAsync_SeekableOverLimit_ThrowsBeforeAnySdkCall()
    {
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "big.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        _s3.PutObjectAsync.DidNotReceive();
    }

    [Fact]
    public async Task SaveAsync_SeekableUnderLimit_Succeeds()
    {
        PutConsumesStream();
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "ok.bin", "docs");

        uri.Scheme.Should().Be("s3");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableOverLimit_ThrowsMidUpload()
    {
        PutConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "stream.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableUnderLimit_Succeeds()
    {
        PutConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "small.bin", "docs");

        uri.Scheme.Should().Be("s3");
    }

    [Fact]
    public async Task SaveAsync_NoLimitConfigured_AllowsLargeFile()
    {
        PutConsumesStream();
        var storage = new S3FileStorage(_s3, new S3StorageOptions { BucketName = "bkt" },
            NullLogger<S3FileStorage>.Instance);
        using var stream = new MemoryStream(new byte[Limit * 4]);

        var uri = await storage.SaveAsync(stream, "huge.bin", "docs");

        uri.Scheme.Should().Be("s3");
    }
}
