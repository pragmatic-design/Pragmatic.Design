using System.IO;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Renci.SshNet;

namespace Pragmatic.Storage.Sftp.Tests;

/// <summary>
///     Covers <c>MaxFileSizeBytes</c> enforcement of <see cref="SftpFileStorage" />: the seekable
///     fast-path rejects before any connection is opened, and non-seekable streams are wrapped in a
///     <see cref="LimitedReadStream" /> that throws mid-upload.
/// </summary>
public sealed class SftpFileStorageLimitTests : SftpStorageTestBase
{
    private const long Limit = 16;

    private SftpFileStorage CreateLimitedStorage()
        => CreateStorage(new SftpStorageOptions
        {
            Host = "host",
            Username = "user",
            Password = "pass",
            MaxFileSizeBytes = Limit,
        });

    /// <summary>Makes the substitute drain the request stream, like a real upload would.</summary>
    private void UploadConsumesStream()
        => Client.UploadFileAsync3.Returns((source, _, _) => source.CopyToAsync(Stream.Null));

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
    public async Task SaveAsync_SeekableOverLimit_ThrowsBeforeConnecting()
    {
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "big.bin", "docs");

        await act.Should().ThrowAsync<FileSizeLimitExceededException>().WithMessage("*exceeds*");
        Factory.Create.DidNotReceive();
    }

    [Fact]
    public async Task SaveAsync_SeekableUnderLimit_Succeeds()
    {
        UploadConsumesStream();
        var storage = CreateLimitedStorage();
        using var stream = new MemoryStream(new byte[Limit - 1]);

        var uri = await storage.SaveAsync(stream, "ok.bin", "docs");

        uri.Scheme.Should().Be("sftp");
    }

    [Fact]
    public async Task SaveAsync_NonSeekableOverLimit_ThrowsMidUpload()
    {
        UploadConsumesStream();
        var storage = CreateLimitedStorage();
        var stream = new NonSeekableStream(new byte[Limit + 8]);

        var act = () => storage.SaveAsync(stream, "stream.bin", "docs");

        await act.Should().ThrowAsync<FileSizeLimitExceededException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task SaveAsync_NoLimitConfigured_AllowsLargeFile()
    {
        UploadConsumesStream();
        var storage = CreateStorage();
        using var stream = new MemoryStream(new byte[Limit * 4]);

        var uri = await storage.SaveAsync(stream, "huge.bin", "docs");

        uri.Scheme.Should().Be("sftp");
    }
}
