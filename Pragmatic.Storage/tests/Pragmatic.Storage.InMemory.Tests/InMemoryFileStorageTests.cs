using System.Text;

namespace Pragmatic.Storage.InMemory.Tests;

public class InMemoryFileStorageTests
{
    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsIdenticalBytes()
    {
        var storage = new InMemoryFileStorage();
        var payload = Encoding.UTF8.GetBytes("hello world");

        var uri = await storage.SaveAsync(new MemoryStream(payload), "greeting.txt", "docs");

        using var stream = await storage.GetAsync(uri);
        stream.Should().NotBeNull();
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task SaveAsync_ReturnsMemUriWithContainerAndExtension()
    {
        var storage = new InMemoryFileStorage();

        var uri = await storage.SaveAsync(Bytes("x"), "photo.png", "avatars");

        uri.Scheme.Should().Be("mem");
        uri.Host.Should().Be("avatars");
        uri.AbsolutePath.Should().EndWith(".png");
    }

    [Fact]
    public async Task GetAsync_MissingFile_ReturnsNull()
    {
        var storage = new InMemoryFileStorage();

        var stream = await storage.GetAsync(new Uri("mem://docs/does-not-exist.txt"));

        stream.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReflectsPresence()
    {
        var storage = new InMemoryFileStorage();
        var uri = await storage.SaveAsync(Bytes("x"), "a.txt", "docs");

        (await storage.ExistsAsync(uri)).Should().BeTrue();
        (await storage.ExistsAsync(new Uri("mem://docs/nope.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndIsIdempotent()
    {
        var storage = new InMemoryFileStorage();
        var uri = await storage.SaveAsync(Bytes("x"), "a.txt", "docs");

        await storage.DeleteAsync(uri);
        (await storage.ExistsAsync(uri)).Should().BeFalse();

        // Second delete on an absent file must not throw.
        await storage.DeleteAsync(uri);
        (await storage.ExistsAsync(uri)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_SameFileNameTwice_ProducesDistinctUris()
    {
        var storage = new InMemoryFileStorage();

        var first = await storage.SaveAsync(Bytes("one"), "same.txt", "docs");
        var second = await storage.SaveAsync(Bytes("two"), "same.txt", "docs");

        first.Should().NotBe(second);
        storage.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ReturnsIndependentStream_DisposeDoesNotAffectLaterGet()
    {
        var storage = new InMemoryFileStorage();
        var payload = Encoding.UTF8.GetBytes("independent");
        var uri = await storage.SaveAsync(new MemoryStream(payload), "a.bin", "docs");

        // First read, fully consumed and disposed.
        var firstStream = await storage.GetAsync(uri);
        firstStream!.Dispose();

        // A second read must still succeed and return the same bytes.
        using var secondStream = await storage.GetAsync(uri);
        secondStream.Should().NotBeNull();
        using var buffer = new MemoryStream();
        await secondStream!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task GetInfoAsync_ReturnsSizeContentTypeAndLastModified()
    {
        var storage = new InMemoryFileStorage();
        var payload = Encoding.UTF8.GetBytes("hello");
        var before = DateTimeOffset.UtcNow;

        var uri = await storage.SaveAsync(new MemoryStream(payload), "note.txt", "docs");

        var info = await storage.GetInfoAsync(uri);
        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(payload.Length);
        info.ContentType.Should().Be("text/plain");
        info.FileUri.Should().Be(uri);
        info.LastModified.Should().NotBeNull();
        info.LastModified!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task GetInfoAsync_MissingFile_ReturnsNull()
    {
        var storage = new InMemoryFileStorage();

        var info = await storage.GetInfoAsync(new Uri("mem://docs/absent.txt"));

        info.Should().BeNull();
    }

    [Fact]
    public async Task Clear_EmptiesTheStore()
    {
        var storage = new InMemoryFileStorage();
        await storage.SaveAsync(Bytes("x"), "a.txt", "docs");
        await storage.SaveAsync(Bytes("y"), "b.txt", "docs");
        storage.Count.Should().Be(2);

        storage.Clear();

        storage.Count.Should().Be(0);
    }
}
