using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the Result-based surface over the optional capabilities:
///     <see cref="FileInfoResultExtensions.GetInfoAsResultAsync" /> (missing file →
///     <see cref="StorageFileNotFoundError" />) and
///     <see cref="SignedUrlResultExtensions.GetDownloadUrlAsResultAsync" /> (a provider that cannot
///     sign → <see cref="StorageWriteError" /> carrying the reason).
/// </summary>
public sealed class CapabilityResultExtensionsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public CapabilityResultExtensionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-cap-{Guid.NewGuid():N}");
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

    // ---- GetInfoAsResultAsync ---------------------------------------------

    [Fact]
    public async Task GetInfoAsResultAsync_ExistingFile_ReturnsSuccessWithInfo()
    {
        using var input = new MemoryStream("hello"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "note.txt", "docs");

        var result = await _storage.GetInfoAsResultAsync(uri);

        result.IsSuccess.Should().BeTrue();
        result.Value.SizeBytes.Should().Be(5);
        result.Value.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task GetInfoAsResultAsync_MissingFile_ReturnsStorageFileNotFoundError()
    {
        var uri = new Uri("/files/docs/missing.txt", UriKind.Relative);

        var result = await _storage.GetInfoAsResultAsync(uri);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageFileNotFoundError>()
            .Which.FileUri.Should().Be(uri);
    }

    [Fact]
    public async Task GetInfoAsResultAsync_ProviderThrows_ReturnsStorageWriteError()
    {
        var fake = new ThrowingInfoProvider(new IOException("stat failed"));

        var result = await fake.GetInfoAsResultAsync(new Uri("/files/docs/x.txt", UriKind.Relative));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>().Which.Reason.Should().Be("stat failed");
    }

    [Fact]
    public async Task GetInfoAsResultAsync_CancelledToken_PropagatesOperationCanceled()
    {
        var fake = new ThrowingInfoProvider(new OperationCanceledException());

        var act = () => fake.GetInfoAsResultAsync(
            new Uri("/files/docs/x.txt", UriKind.Relative),
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- GetDownloadUrlAsResultAsync --------------------------------------

    [Fact]
    public async Task GetDownloadUrlAsResultAsync_Supported_ReturnsSuccessWithUrl()
    {
        var expected = new Uri("https://cdn.example.com/photos/x.jpg?sig=abc");
        var provider = new StubSignedUrlProvider(_ => expected);

        var result = await provider.GetDownloadUrlAsResultAsync(
            new Uri("s3://bkt/photos/x.jpg"), TimeSpan.FromMinutes(15));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task GetDownloadUrlAsResultAsync_NotSupported_ReturnsStorageWriteErrorWithReason()
    {
        var provider = new StubSignedUrlProvider(
            _ => throw new NotSupportedException("no shared key credential"));

        var result = await provider.GetDownloadUrlAsResultAsync(
            new Uri("s3://bkt/photos/x.jpg"), TimeSpan.FromMinutes(15));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<StorageWriteError>()
            .Which.Reason.Should().Be("no shared key credential");
    }

    [Fact]
    public async Task GetDownloadUrlAsResultAsync_CancelledToken_PropagatesOperationCanceled()
    {
        var provider = new StubSignedUrlProvider(_ => throw new OperationCanceledException());

        var act = () => provider.GetDownloadUrlAsResultAsync(
            new Uri("s3://bkt/photos/x.jpg"), TimeSpan.FromMinutes(15),
            new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- Helpers ----------------------------------------------------------

    private sealed class ThrowingInfoProvider(Exception toThrow) : IFileInfoProvider
    {
        public Task<StoredFileInfo?> GetInfoAsync(Uri fileUri, CancellationToken ct = default)
            => throw toThrow;
    }

    private sealed class StubSignedUrlProvider(Func<Uri, Uri> factory) : ISignedUrlProvider
    {
        public Task<Uri> GetDownloadUrlAsync(Uri fileUri, TimeSpan expiry, CancellationToken ct = default)
            => Task.FromResult(factory(fileUri));
    }
}
