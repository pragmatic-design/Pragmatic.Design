using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the <see cref="IFileInfoProvider" /> capability of <see cref="LocalDiskFileStorage" />:
///     <see cref="LocalDiskFileStorage.GetInfoAsync" /> reports size / content-type / last-modified
///     for a stored file, and returns null for a missing file or an unsafe (traversal) URI.
/// </summary>
public sealed class LocalDiskFileStorageInfoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageInfoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-info-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task GetInfoAsync_SavedFile_ReturnsSizeContentTypeAndLastModified()
    {
        var content = "the quick brown fox"u8.ToArray();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        using var input = new MemoryStream(content);
        var uri = await _storage.SaveAsync(input, "note.txt", "docs");

        var info = await _storage.GetInfoAsync(uri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(content.Length);
        info.ContentType.Should().Be("text/plain");
        info.FileUri.Should().Be(uri);
        info.LastModified.Should().NotBeNull();
        info.LastModified!.Value.Should().BeAfter(before);
    }

    [Fact]
    public async Task GetInfoAsync_UnknownExtension_ReturnsOctetStreamContentType()
    {
        using var input = new MemoryStream([1, 2, 3, 4]);
        var uri = await _storage.SaveAsync(input, "blob.bin", "docs");

        var info = await _storage.GetInfoAsync(uri);

        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4);
        info.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetInfoAsync_MissingFile_ReturnsNull()
    {
        var uri = new Uri("/files/docs/does-not-exist.txt", UriKind.Relative);

        var info = await _storage.GetInfoAsync(uri);

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_PathTraversalUri_ReturnsNull()
    {
        var uri = new Uri("/../../../Windows/win.ini", UriKind.Relative);

        var info = await _storage.GetInfoAsync(uri);

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetInfoAsync_CancelledToken_Throws()
    {
        using var input = new MemoryStream("x"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "c.txt", "docs");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _storage.GetInfoAsync(uri, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
