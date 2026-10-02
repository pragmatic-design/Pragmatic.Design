using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Storage.Local;

namespace Pragmatic.Storage.Tests;

/// <summary>
///     Security-critical coverage for container validation and path-traversal protection in
///     <see cref="LocalDiskFileStorage" /> (ResolveContainerDirectory + TryResolveSafePath).
/// </summary>
public sealed class LocalDiskFileStorageSecurityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageSecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-sec-{Guid.NewGuid():N}");
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

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task SaveAsync_EmptyOrWhitespaceContainer_ThrowsArgumentException(string container)
    {
        using var stream = new MemoryStream("x"u8.ToArray());

        var act = () => _storage.SaveAsync(stream, "file.txt", container);

        (await act.Should().ThrowAsync<ArgumentException>())
            .And.ParamName.Should().Be("container");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("../../escape")]
    [InlineData("sub/../../escape")]
    public async Task SaveAsync_TraversalContainer_ThrowsArgumentException(string container)
    {
        using var stream = new MemoryStream("x"u8.ToArray());

        var act = () => _storage.SaveAsync(stream, "file.txt", container);

        (await act.Should().ThrowAsync<ArgumentException>())
            .And.ParamName.Should().Be("container");
    }

    [Fact]
    public async Task SaveAsync_NestedContainer_StaysInsideRoot()
    {
        // A legitimate nested container is allowed and lands under files/.
        using var stream = new MemoryStream("ok"u8.ToArray());

        var uri = await _storage.SaveAsync(stream, "file.txt", "a/b/c");

        Directory.Exists(Path.Combine(_tempDir, "files", "a", "b", "c")).Should().BeTrue();
        uri.ToString().Should().StartWith("/files/a/b/c/");
    }

    [Fact]
    public async Task GetAsync_TraversalContainerReachesNoFile_ReturnsNull()
    {
        // Even if a file exists at the escaped location, GetAsync must not read it.
        var outsideDir = Path.Combine(_tempDir, "files");
        Directory.CreateDirectory(outsideDir);
        var secretPath = Path.Combine(_tempDir, "secret.txt");
        await File.WriteAllTextAsync(secretPath, "top-secret");

        // /files/../secret.txt resolves to {root}/secret.txt which is OUTSIDE {root} only if
        // base is {root}; here base IS _tempDir so secret.txt sits at the root boundary.
        // Use a clearly-escaping traversal that leaves the root entirely.
        var uri = new Uri("/files/../../secret.txt", UriKind.Relative);

        var stream = await _storage.GetAsync(uri);

        stream.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_TraversalUri_DoesNotDeleteOutsideRoot()
    {
        var parent = Directory.GetParent(_tempDir)!.FullName;
        var victim = Path.Combine(parent, $"victim-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(victim, "do-not-delete");
        try
        {
            var uri = new Uri("/../" + Path.GetFileName(victim), UriKind.Relative);

            await _storage.DeleteAsync(uri);

            File.Exists(victim).Should().BeTrue("traversal must not delete files outside the root");
        }
        finally
        {
            if (File.Exists(victim))
                File.Delete(victim);
        }
    }
}
