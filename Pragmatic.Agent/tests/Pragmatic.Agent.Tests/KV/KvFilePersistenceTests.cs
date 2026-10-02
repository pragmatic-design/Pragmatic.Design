using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

public class KvFilePersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kvPath;

    public KvFilePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pragmatic-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kvPath = Path.Combine(_tempDir, "kv.json");
    }

    [Fact]
    public void FlushNow_CreatesFileWithEntries()
    {
        var store = new KvStore();
        store.Set("key1", "value1");
        store.Set("key2", "value2");

        var persistence = new KvFilePersistence(store, _kvPath);
        persistence.FlushNow();

        File.Exists(_kvPath).Should().BeTrue();
        var content = File.ReadAllText(_kvPath);
        content.Should().Contain("key1");
        content.Should().Contain("value1");
    }

    [Fact]
    public void Load_RestoresFromFile()
    {
        // Write some entries
        var store1 = new KvStore();
        store1.Set("config/host", "localhost");
        store1.Set("flags/new-ui", "true");
        var persistence1 = new KvFilePersistence(store1, _kvPath);
        persistence1.FlushNow();

        // Load into new store
        var store2 = new KvStore();
        var persistence2 = new KvFilePersistence(store2, _kvPath);
        persistence2.Load();

        store2.Get("config/host")!.Value.Should().Be("localhost");
        store2.Get("flags/new-ui")!.Value.Should().Be("true");
        store2.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Load_MissingFile_DoesNotThrow()
    {
        var store = new KvStore();
        var persistence = new KvFilePersistence(store, Path.Combine(_tempDir, "nonexistent.json"));

        var act = () => persistence.Load();
        act.Should().NotThrow();
        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void FlushNow_AtomicWrite_NoCorruption()
    {
        var store = new KvStore();
        for (var i = 0; i < 100; i++)
            store.Set($"key-{i}", $"value-{i}");

        var persistence = new KvFilePersistence(store, _kvPath);
        persistence.FlushNow();

        // Verify no temp file left behind
        File.Exists(_kvPath + ".tmp").Should().BeFalse();

        // Verify all entries present
        var store2 = new KvStore();
        new KvFilePersistence(store2, _kvPath).Load();
        store2.GetAll().Should().HaveCount(100);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup */ }
    }
}
