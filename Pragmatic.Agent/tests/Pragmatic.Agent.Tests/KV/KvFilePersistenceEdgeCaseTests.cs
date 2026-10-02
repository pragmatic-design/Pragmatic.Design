using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

/// <summary>
///     Corruption and lifecycle edge cases for <see cref="KvFilePersistence"/> that complement the
///     existing happy-path round-trip tests: malformed/empty/null files (must not crash — the agent
///     starts with an empty KV), atomic-write integrity under repeated flushes, directory creation,
///     and dispose-time final flush. All file I/O is confined to a per-test temp directory.
/// </summary>
public class KvFilePersistenceEdgeCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _kvPath;

    public KvFilePersistenceEdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kv-edge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _kvPath = Path.Combine(_tempDir, "kv.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Load_CorruptJson_DoesNotThrow_LeavesStoreEmpty()
    {
        File.WriteAllText(_kvPath, "{ this is not valid json ]");
        var store = new KvStore();
        var persistence = new KvFilePersistence(store, _kvPath);

        // Persistence swallows parse failures so a poisoned file can't block startup.
        var act = () => persistence.Load();

        act.Should().NotThrow();
        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Load_JsonNullLiteral_LeavesStoreEmpty()
    {
        File.WriteAllText(_kvPath, "null");
        var store = new KvStore();

        new KvFilePersistence(store, _kvPath).Load();

        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Load_EmptyArray_LeavesStoreEmpty()
    {
        File.WriteAllText(_kvPath, "[]");
        var store = new KvStore();

        new KvFilePersistence(store, _kvPath).Load();

        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Load_EmptyFile_DoesNotThrow()
    {
        File.WriteAllText(_kvPath, string.Empty);
        var store = new KvStore();

        var act = () => new KvFilePersistence(store, _kvPath).Load();

        act.Should().NotThrow();
        store.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void FlushNow_PreservesVersionAcrossReload()
    {
        var source = new KvStore();
        var (version, _) = source.Set("config/host", "localhost");
        new KvFilePersistence(source, _kvPath).FlushNow();

        var target = new KvStore();
        new KvFilePersistence(target, _kvPath).Load();

        target.Get("config/host")!.Version.Should().Be(version);
    }

    [Fact]
    public void FlushNow_CreatesMissingDirectory()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "deep", "kv.json");
        var store = new KvStore();
        store.Set("k", "v");

        new KvFilePersistence(store, nestedPath).FlushNow();

        File.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public void FlushNow_RepeatedFlushes_NoTempLeftover_AllEntriesIntact()
    {
        var store = new KvStore();
        for (var i = 0; i < 50; i++)
            store.Set($"key-{i}", $"value-{i}");

        var persistence = new KvFilePersistence(store, _kvPath);
        persistence.FlushNow();
        persistence.FlushNow();
        persistence.FlushNow();

        // Atomic temp+rename must not leave a scratch file behind...
        File.Exists(_kvPath + ".tmp").Should().BeFalse();

        // ...and every entry survives a reload.
        var reloaded = new KvStore();
        new KvFilePersistence(reloaded, _kvPath).Load();
        reloaded.GetAll().Should().HaveCount(50);
    }

    [Fact]
    public void Dispose_FlushesFinalStateToDisk()
    {
        var store = new KvStore();
        store.Set("flags/x", "true");
        var persistence = new KvFilePersistence(store, _kvPath);

        // Dispose performs a final flush even without StartPeriodicFlush.
        persistence.Dispose();

        File.Exists(_kvPath).Should().BeTrue();
        var reloaded = new KvStore();
        new KvFilePersistence(reloaded, _kvPath).Load();
        reloaded.Get("flags/x")!.Value.Should().Be("true");
    }

    [Fact]
    public void FlushNow_ThenLoadIntoFreshStore_RoundTripsValue()
    {
        var source = new KvStore();
        source.Set("config/region", "eu-west");
        new KvFilePersistence(source, _kvPath).FlushNow();

        var target = new KvStore();
        new KvFilePersistence(target, _kvPath).Load();

        target.Get("config/region")!.Value.Should().Be("eu-west");
    }
}
