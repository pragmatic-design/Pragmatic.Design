using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;

namespace Pragmatic.Storage.Azure.Tests;

/// <summary>
///     Covers the <c>MaxFileSizeBytes</c> enforcement of <see cref="AzureBlobFileStorage" />
///     on both the seekable fast-path (rejected before any SDK call) and the streaming
///     (non-seekable) path via <see cref="LimitedReadStream" />.
/// </summary>
public sealed class AzureBlobFileStorageLimitTests
{
    private const long Limit = 16;
    private readonly AzureStorageSubstituteFixture _fixture = new();

    private AzureBlobFileStorage CreateLimitedStorage()
        => _fixture.CreateStorage(new AzureBlobStorageOptions { MaxFileSizeBytes = Limit });

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
        _fixture.Service.GetBlobContainerClientSetup.DidNotReceive();
    }

    [Fact]
    public async Task SaveAsync_SeekableUnderLimit_Uploads()
    {
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "ok.bin", "docs");

        uri.Should().Be(_fixture.BlobUri);
    }

    [Fact]
    public async Task SaveAsync_NonSeekableOverLimit_ThrowsMidUpload()
    {
        _fixture.UploadConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "stream.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableUnderLimit_Succeeds()
    {
        _fixture.UploadConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "small.bin", "docs");

        uri.Should().Be(_fixture.BlobUri);
    }

    [Fact]
    public async Task SaveAsync_NoLimitConfigured_AllowsLargeFile()
    {
        var storage = _fixture.CreateStorage();
        using var stream = new MemoryStream(new byte[Limit * 4]);

        var uri = await storage.SaveAsync(stream, "huge.bin", "docs");

        uri.Should().Be(_fixture.BlobUri);
    }
}
