using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Result;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the Result-based surface (<see cref="FileStorageResultExtensions" />) layered over the
///     throwing <see cref="IFileStorage" /> contract: happy paths against a real
///     <see cref="LocalDiskFileStorage" /> plus a fake storage for the hard-to-provoke error branches.
/// </summary>
public sealed class FileStorageResultExtensionsTests : IDisposable
{
    private const long Limit = 16;
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;
    private readonly LocalDiskFileStorage _limited;

    public FileStorageResultExtensionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-result-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance);
        _limited = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance, Limit);
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

    // ---- Save -------------------------------------------------------------

    [Fact]
    public async Task SaveAsResultAsync_ValidContent_ReturnsSuccessWithUri()
    {
        using var input = new MemoryStream("hello"u8.ToArray());

        var result = await _storage.SaveAsResultAsync(input, "note.txt", "docs");

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().StartWith("/files/docs/");
        (await _storage.ExistsAsync(result.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsResultAsync_SeekableOverLimit_ReturnsFileTooLargeError()
    {
        using var input = new MemoryStream(new byte[Limit + 8]);

        var result = await _limited.SaveAsResultAsync(input, "big.bin", "docs");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<FileTooLargeError>()
            .Which.LimitBytes.Should().Be(Limit);
        ((FileTooLargeError)result.Error).ActualBytes.Should().Be(Limit + 8);
    }

    [Fact]
    public async Task SaveAsResultAsync_NonSeekableOverLimit_ReturnsFileTooLargeError()
    {
        var input = new NonSeekableStream(new byte[Limit + 8]);

        var result = await _limited.SaveAsResultAsync(input, "stream.bin", "docs");

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<FileTooLargeError>().Which;
        error.LimitBytes.Should().Be(Limit);
        // Actual size is unknown for a non-seekable stream.
        error.ActualBytes.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsResultAsync_InvalidContainer_ReturnsStorageWriteError()
    {
        using var input = new MemoryStream("x"u8.ToArray());

        // A traversal container is rejected by the provider with ArgumentException.
        var result = await _storage.SaveAsResultAsync(input, "x.txt", "../escape");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>();
    }

    [Fact]
    public async Task SaveAsResultAsync_ProviderThrowsIOException_ReturnsStorageWriteErrorWithReasonOnly()
    {
        var fake = new ThrowingFileStorage(new IOException("disk on fire"));
        using var input = new MemoryStream("x"u8.ToArray());

        var result = await fake.SaveAsResultAsync(input, "x.txt", "docs");

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<StorageWriteError>().Which;
        error.Reason.Should().Be("disk on fire");
        error.Reason.Should().NotContain("at "); // no stack trace leaked
        error.IsTransient.Should().BeTrue();      // IOException is transient
    }

    [Fact]
    public async Task SaveAsResultAsync_CancelledToken_PropagatesOperationCanceled()
    {
        var fake = new ThrowingFileStorage(new OperationCanceledException());
        using var input = new MemoryStream("x"u8.ToArray());

        var act = () => fake.SaveAsResultAsync(input, "x.txt", "docs", new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Get --------------------------------------------------------------

    [Fact]
    public async Task GetAsResultAsync_ExistingFile_ReturnsSuccessWithReadableStream()
    {
        var content = "round-trip"u8.ToArray();
        using var input = new MemoryStream(content);
        var uri = await _storage.SaveAsync(input, "doc.txt", "docs");

        var result = await _storage.GetAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
        Stream stream = result.Value;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(content);
        }
    }

    [Fact]
    public async Task GetAsResultAsync_MissingFile_ReturnsStorageFileNotFoundError()
    {
        var uri = new Uri("/files/docs/missing.txt", UriKind.Relative);

        var result = await _storage.GetAsResultAsync(uri);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageFileNotFoundError>()
            .Which.FileUri.Should().Be(uri);
    }

    [Fact]
    public async Task GetAsResultAsync_ProviderThrows_ReturnsStorageWriteError()
    {
        var fake = new ThrowingFileStorage(new IOException("read failed"));

        var result = await fake.GetAsResultAsync(new Uri("/files/docs/x.txt", UriKind.Relative));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>().Which.Reason.Should().Be("read failed");
    }

    // ---- Exists -----------------------------------------------------------

    [Fact]
    public async Task ExistsAsResultAsync_ExistingFile_ReturnsSuccessTrue()
    {
        using var input = new MemoryStream("y"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "present.txt", "docs");

        var result = await _storage.ExistsAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsResultAsync_MissingFile_ReturnsSuccessFalse()
    {
        var uri = new Uri("/files/docs/nope.txt", UriKind.Relative);

        var result = await _storage.ExistsAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsResultAsync_ProviderThrows_ReturnsStorageWriteError()
    {
        var fake = new ThrowingFileStorage(new InvalidOperationException("boom"));

        var result = await fake.ExistsAsResultAsync(new Uri("/files/docs/x.txt", UriKind.Relative));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>();
    }

    // ---- Delete -----------------------------------------------------------

    [Fact]
    public async Task DeleteAsResultAsync_ExistingFile_ReturnsSuccess()
    {
        using var input = new MemoryStream("z"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "gone.txt", "docs");

        var result = await _storage.DeleteAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
        (await _storage.ExistsAsync(uri)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsResultAsync_MissingFile_ReturnsSuccessIdempotent()
    {
        var uri = new Uri("/files/docs/absent.txt", UriKind.Relative);

        var result = await _storage.DeleteAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsResultAsync_ProviderThrows_ReturnsStorageWriteError()
    {
        var fake = new ThrowingFileStorage(new IOException("delete failed"));

        var result = await fake.DeleteAsResultAsync(new Uri("/files/docs/x.txt", UriKind.Relative));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>().Which.Reason.Should().Be("delete failed");
    }

    [Fact]
    public async Task DeleteAsResultAsync_ProviderObservesCancellation_PropagatesOperationCanceled()
    {
        // A cancellation must never be swallowed into a StorageWriteError failure.
        var fake = new ThrowingFileStorage(new OperationCanceledException());

        var act = () => fake.DeleteAsResultAsync(
            new Uri("/files/docs/x.txt", UriKind.Relative),
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Helpers ----------------------------------------------------------

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

    /// <summary>A fake storage whose every operation throws the supplied exception.</summary>
    private sealed class ThrowingFileStorage(Exception toThrow) : IFileStorage
    {
        public Task<Uri> SaveAsync(Stream content, string fileName, string container, CancellationToken ct = default)
            => throw toThrow;

        public Task<Stream?> GetAsync(Uri fileUri, CancellationToken ct = default)
            => throw toThrow;

        public Task<bool> ExistsAsync(Uri fileUri, CancellationToken ct = default)
            => throw toThrow;

        public Task DeleteAsync(Uri fileUri, CancellationToken ct = default)
            => throw toThrow;
    }
}
