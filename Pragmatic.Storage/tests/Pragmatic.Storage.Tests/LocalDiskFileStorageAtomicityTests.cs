using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the atomic-write behavior of <see cref="LocalDiskFileStorage.SaveAsync" />:
///     content is written to a temp file and renamed into place, so no <c>.tmp</c> file
///     survives a successful save and no final file appears after a mid-copy failure.
/// </summary>
public sealed class LocalDiskFileStorageAtomicityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageAtomicityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance);
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

    /// <summary>A stream that throws after yielding some bytes, simulating a mid-copy failure.</summary>
    private sealed class FailingStream(int bytesBeforeFailure) : Stream
    {
        private int _served;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served >= bytesBeforeFailure)
                throw new IOException("simulated stream failure");
            var toServe = Math.Min(count, bytesBeforeFailure - _served);
            _served += toServe;
            return toServe;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private string ContainerDir => Path.Combine(_tempDir, "files", "docs");

    [Fact]
    public async Task SaveAsync_Success_LeavesNoTempFile()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var uri = await _storage.SaveAsync(stream, "ok.bin", "docs");

        (await _storage.ExistsAsync(uri)).Should().BeTrue();
        Directory.GetFiles(ContainerDir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_MidCopyFailure_LeavesNoFinalFileAndNoTempFile()
    {
        var stream = new FailingStream(bytesBeforeFailure: 8);

        var act = () => _storage.SaveAsync(stream, "broken.bin", "docs");

        await act.Should().ThrowAsync<IOException>();
        Directory.GetFiles(ContainerDir).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_Success_StoresFullContentAtFinalPath()
    {
        byte[] payload = [10, 20, 30, 40];
        using var stream = new MemoryStream(payload);

        var uri = await _storage.SaveAsync(stream, "data.bin", "docs");

        var readBack = await _storage.GetAsync(uri);
        readBack.Should().NotBeNull();
        await using (readBack!.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await readBack!.CopyToAsync(ms);
            ms.ToArray().Should().Equal(payload);
        }
    }
}
