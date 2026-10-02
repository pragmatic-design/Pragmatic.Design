using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the <c>maxFileSizeBytes</c> enforcement of <see cref="LocalDiskFileStorage" />
///     on both the seekable fast-path and the streaming (non-seekable) path.
/// </summary>
public sealed class LocalDiskFileStorageLimitTests : IDisposable
{
    private const long Limit = 16;
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageLimitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance, Limit);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

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
    public async Task SaveAsync_SeekableUnderLimit_Succeeds()
    {
        using var stream = new MemoryStream(new byte[Limit - 1]);

        var uri = await _storage.SaveAsync(stream, "ok.bin", "docs");

        uri.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_SeekableOverLimit_ThrowsAndLeavesNoFile()
    {
        using var stream = new MemoryStream(new byte[Limit + 8]);

        var act = () => _storage.SaveAsync(stream, "big.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        // The container dir may not even be created; if it is, it must be empty.
        var dir = Path.Combine(_tempDir, "files", "docs");
        if (Directory.Exists(dir))
            Directory.GetFiles(dir).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_NonSeekableOverLimit_ThrowsAndCleansUp()
    {
        var stream = new NonSeekableStream(new byte[Limit + 8]);

        var act = () => _storage.SaveAsync(stream, "stream.bin", "docs");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
        // The partial/oversized file must be cleaned up on failure.
        var dir = Path.Combine(_tempDir, "files", "docs");
        if (Directory.Exists(dir))
            Directory.GetFiles(dir).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_NonSeekableUnderLimit_Succeeds()
    {
        var stream = new NonSeekableStream(new byte[Limit - 1]);

        var uri = await _storage.SaveAsync(stream, "small.bin", "docs");

        (await _storage.ExistsAsync(uri)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_NoLimitConfigured_AllowsLargeFile()
    {
        var unlimited = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance);
        using var stream = new MemoryStream(new byte[Limit * 4]);

        var uri = await unlimited.SaveAsync(stream, "huge.bin", "docs");

        (await unlimited.ExistsAsync(uri)).Should().BeTrue();
    }
}
