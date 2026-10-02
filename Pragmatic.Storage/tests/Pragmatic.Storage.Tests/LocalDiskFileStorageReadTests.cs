using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Covers the read-side of <see cref="LocalDiskFileStorage" /> (GetAsync / ExistsAsync),
///     which the previous test suite left untested.
/// </summary>
public sealed class LocalDiskFileStorageReadTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageReadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-read-{Guid.NewGuid():N}");
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
            // Best-effort cleanup; a transient lock must not fail the suite.
        }
    }

    [Fact]
    public async Task GetAsync_SavedFile_ReturnsStreamWithSameContent()
    {
        var content = "round-trip content"u8.ToArray();
        using var input = new MemoryStream(content);
        var uri = await _storage.SaveAsync(input, "doc.txt", "docs");

        var stream = await _storage.GetAsync(uri);

        stream.Should().NotBeNull();
        Stream readStream = stream!;
        await using (readStream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await readStream.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(content);
        }
    }

    [Fact]
    public async Task GetAsync_MissingFile_ReturnsNull()
    {
        var uri = new Uri("/files/docs/does-not-exist.txt", UriKind.Relative);

        var stream = await _storage.GetAsync(uri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_PathTraversalUri_ReturnsNull()
    {
        // A traversal attempt must be rejected by TryResolveSafePath, yielding null
        // rather than reading a file outside the storage root.
        var uri = new Uri("/../../../Windows/win.ini", UriKind.Relative);

        var stream = await _storage.GetAsync(uri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_CancelledToken_Throws()
    {
        using var input = new MemoryStream("x"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "c.txt", "docs");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _storage.GetAsync(uri, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExistsAsync_SavedFile_ReturnsTrue()
    {
        using var input = new MemoryStream("y"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "present.txt", "docs");

        (await _storage.ExistsAsync(uri)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingFile_ReturnsFalse()
    {
        var uri = new Uri("/files/docs/nope.txt", UriKind.Relative);

        (await _storage.ExistsAsync(uri)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_DeletedFile_ReturnsFalse()
    {
        using var input = new MemoryStream("z"u8.ToArray());
        var uri = await _storage.SaveAsync(input, "gone.txt", "docs");
        await _storage.DeleteAsync(uri);

        (await _storage.ExistsAsync(uri)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_PathTraversalUri_ReturnsFalse()
    {
        var uri = new Uri("/../../secret.txt", UriKind.Relative);

        (await _storage.ExistsAsync(uri)).Should().BeFalse();
    }
}
