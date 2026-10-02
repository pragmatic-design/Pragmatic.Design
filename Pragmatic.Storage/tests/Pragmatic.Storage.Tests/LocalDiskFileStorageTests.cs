using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

public class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalDiskFileStorage(_tempDir, NullLogger<LocalDiskFileStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_CreatesFileOnDisk()
    {
        using var stream = new MemoryStream("hello world"u8.ToArray());

        var uri = await _storage.SaveAsync(stream, "test.txt", "docs");

        uri.Should().NotBeNull();
        var files = Directory.GetFiles(Path.Combine(_tempDir, "files", "docs"));
        files.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveAsync_ReturnsRelativeUri()
    {
        using var stream = new MemoryStream("data"u8.ToArray());

        var uri = await _storage.SaveAsync(stream, "photo.jpg", "photos");

        uri.IsAbsoluteUri.Should().BeFalse();
        uri.ToString().Should().StartWith("/files/photos/");
        uri.ToString().Should().EndWith(".jpg");
    }

    [Fact]
    public async Task SaveAsync_PreservesFileContent()
    {
        var content = "file content for test"u8.ToArray();
        using var stream = new MemoryStream(content);

        var uri = await _storage.SaveAsync(stream, "data.bin", "uploads");

        var relativePath = uri.ToString().TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_tempDir, relativePath);
        var saved = await File.ReadAllBytesAsync(fullPath);
        saved.Should().Equal(content);
    }

    [Fact]
    public async Task SaveAsync_CreatesContainerDirectory()
    {
        using var stream = new MemoryStream("x"u8.ToArray());

        await _storage.SaveAsync(stream, "file.txt", "new-container");

        Directory.Exists(Path.Combine(_tempDir, "files", "new-container")).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_MultipleFiles_NoDuplicateNames()
    {
        using var s1 = new MemoryStream("a"u8.ToArray());
        using var s2 = new MemoryStream("b"u8.ToArray());

        var uri1 = await _storage.SaveAsync(s1, "file.txt", "docs");
        var uri2 = await _storage.SaveAsync(s2, "file.txt", "docs");

        uri1.Should().NotBe(uri2);
        Directory.GetFiles(Path.Combine(_tempDir, "files", "docs")).Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        using var stream = new MemoryStream("to delete"u8.ToArray());
        var uri = await _storage.SaveAsync(stream, "temp.txt", "trash");

        await _storage.DeleteAsync(uri);

        Directory.GetFiles(Path.Combine(_tempDir, "files", "trash")).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_DoesNotThrow()
    {
        var uri = new Uri("/files/gone/missing.txt", UriKind.Relative);

        var act = () => _storage.DeleteAsync(uri);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_WithCancellationToken_Cancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stream = new MemoryStream(new byte[1024]);

        var act = () => _storage.SaveAsync(stream, "big.bin", "uploads", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
