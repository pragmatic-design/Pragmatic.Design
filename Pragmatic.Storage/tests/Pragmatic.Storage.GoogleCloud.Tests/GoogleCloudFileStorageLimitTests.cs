using Google.Apis.Upload;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StorageObject = Google.Apis.Storage.v1.Data.Object;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>
///     Covers the <c>MaxFileSizeBytes</c> enforcement of <see cref="GoogleCloudFileStorage" /> on
///     both the seekable fast-path (rejected before any SDK call) and the streaming (non-seekable)
///     path via <see cref="LimitedReadStream" />.
/// </summary>
public sealed class GoogleCloudFileStorageLimitTests
{
    private const long Limit = 16;
    private readonly StorageClientMock _gcs = new StorageClientMock();

    private GoogleCloudFileStorage CreateLimitedStorage()
        => new(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt", MaxFileSizeBytes = Limit },
            NullLogger<GoogleCloudFileStorage>.Instance);

    /// <summary>Configures the substitute to fully consume the upload stream, like the real SDK.</summary>
    private void UploadConsumesStream()
        => _gcs.UploadObjectAsync7Setup.Returns(args =>
        {
            ((Stream)args[3]!).CopyTo(Stream.Null);
            return Task.FromResult(new StorageObject());
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
        _gcs.UploadObjectAsync7Setup.DidNotReceive();
    }

    [Fact]
    public async Task SaveAsync_SeekableUnderLimit_Succeeds()
    {
        UploadConsumesStream();
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "ok.bin", "docs");

        uri.Scheme.Should().Be("gs");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableOverLimit_ThrowsMidUpload()
    {
        UploadConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "stream.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableUnderLimit_Succeeds()
    {
        UploadConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "small.bin", "docs");

        uri.Scheme.Should().Be("gs");
    }

    [Fact]
    public async Task SaveAsync_NoLimitConfigured_AllowsLargeFile()
    {
        UploadConsumesStream();
        var storage = new GoogleCloudFileStorage(_gcs, new GoogleCloudStorageOptions { BucketName = "bkt" },
            NullLogger<GoogleCloudFileStorage>.Instance);
        using var stream = new MemoryStream(new byte[Limit * 4]);

        var uri = await storage.SaveAsync(stream, "huge.bin", "docs");

        uri.Scheme.Should().Be("gs");
    }
}
