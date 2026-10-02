using System.Threading.Channels;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

public class KvStoreTests
{
    private readonly KvStore _store = new();

    [Fact]
    public void Get_NonExistentKey_ReturnsNull()
    {
        _store.Get("missing").Should().BeNull();
    }

    [Fact]
    public void Set_And_Get_ReturnsValue()
    {
        _store.Set("key1", "value1");

        var entry = _store.Get("key1");
        entry.Should().NotBeNull();
        entry!.Value.Should().Be("value1");
        entry.Version.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Set_IncreasesVersion()
    {
        var (v1, _) = _store.Set("key1", "value1");
        var (v2, _) = _store.Set("key2", "value2");

        v2.Should().BeGreaterThan(v1);
    }

    [Fact]
    public void Set_CAS_Success()
    {
        var (v1, _) = _store.Set("key1", "value1");
        var (v2, conflict) = _store.Set("key1", "value2", expectedVersion: v1);

        conflict.Should().BeFalse();
        v2.Should().BeGreaterThan(v1);
        _store.Get("key1")!.Value.Should().Be("value2");
    }

    [Fact]
    public void Set_CAS_Conflict()
    {
        _store.Set("key1", "value1");
        var (_, conflict) = _store.Set("key1", "value2", expectedVersion: 999);

        conflict.Should().BeTrue();
        _store.Get("key1")!.Value.Should().Be("value1");
    }

    [Fact]
    public void Delete_ExistingKey_ReturnsTrue()
    {
        _store.Set("key1", "value1");

        _store.Delete("key1").Should().BeTrue();
        _store.Get("key1").Should().BeNull();
    }

    [Fact]
    public void Delete_NonExistentKey_ReturnsFalse()
    {
        _store.Delete("missing").Should().BeFalse();
    }

    [Fact]
    public void GetByPrefix_FiltersCorrectly()
    {
        _store.Set("config/a", "1");
        _store.Set("config/b", "2");
        _store.Set("flags/x", "true");

        var configEntries = _store.GetByPrefix("config/");
        configEntries.Should().HaveCount(2);
        configEntries.Select(e => e.Key).Should().Contain(["config/a", "config/b"]);
    }

    [Fact]
    public void SetIfNewer_NewerVersion_Applies()
    {
        _store.Set("key1", "old");
        var applied = _store.SetIfNewer("key1", "new", 9999, DateTimeOffset.UtcNow);

        applied.Should().BeTrue();
        _store.Get("key1")!.Value.Should().Be("new");
    }

    [Fact]
    public void SetIfNewer_OlderVersion_Rejected()
    {
        var (currentVersion, _) = _store.Set("key1", "current");
        var applied = _store.SetIfNewer("key1", "old", currentVersion - 1, DateTimeOffset.UtcNow);

        applied.Should().BeFalse();
        _store.Get("key1")!.Value.Should().Be("current");
    }

    [Fact]
    public void DeleteIfNewer_NewerVersion_RemovesEntry()
    {
        var (currentVersion, _) = _store.Set("routes/api", "backend-a");

        _store.DeleteIfNewer("routes/api", currentVersion + 1).Should().BeTrue();
        _store.Get("routes/api").Should().BeNull();
    }

    [Fact]
    public void DeleteIfNewer_OlderVersion_IsNoOp()
    {
        // A forged/stale gossip delete carrying an older version must NOT wipe a newer entry
        // (config-wipe DoS guard).
        var (currentVersion, _) = _store.Set("routes/api", "backend-a");

        _store.DeleteIfNewer("routes/api", currentVersion - 1).Should().BeFalse();
        _store.Get("routes/api").Should().NotBeNull();
        _store.Get("routes/api")!.Value.Should().Be("backend-a");
    }

    [Fact]
    public void DeleteIfNewer_SameVersion_IsNoOp()
    {
        var (currentVersion, _) = _store.Set("routes/api", "backend-a");

        _store.DeleteIfNewer("routes/api", currentVersion).Should().BeFalse();
        _store.Get("routes/api").Should().NotBeNull();
    }

    [Fact]
    public void DeleteIfNewer_MissingKey_IsNoOp()
    {
        _store.DeleteIfNewer("missing", 1).Should().BeFalse();
    }

    [Fact]
    public void SecretKey_IsEncryptedAtRest_AndDecryptedOnGet()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++) key[i] = (byte)i;
        var store = new KvStore(new KvSecretProtector(key));

        store.Set("secret/api-token", "super-sensitive");

        // Get returns plaintext...
        store.Get("secret/api-token")!.Value.Should().Be("super-sensitive");

        // ...but the value held at rest (and thus replicated over gossip / persisted) is ciphertext.
        var raw = store.GetAll().Single(e => e.Key == "secret/api-token");
        raw.Value.Should().NotBe("super-sensitive");
        raw.Value.Should().NotContain("super-sensitive");
    }

    [Fact]
    public void SetIfNewer_SecretCiphertext_IsStoredVerbatim_NotDoubleEncrypted()
    {
        // Gossip ingestion: the incoming secret value is already ciphertext from the origin node.
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
        var encryptor = new KvSecretProtector(key);
        var store = new KvStore(encryptor);

        var ciphertext = encryptor.Encrypt("from-peer");
        store.SetIfNewer("secret/peer-token", ciphertext, 5000, DateTimeOffset.UtcNow);

        // Get decrypts once → original plaintext (proves no double-encryption on the gossip path).
        store.Get("secret/peer-token")!.Value.Should().Be("from-peer");
    }

    [Fact]
    public void Watch_ReceivesChanges()
    {
        var channel = Channel.CreateUnbounded<KvChangeEvent>();
        using var sub = _store.Watch("config/", channel);

        _store.Set("config/key1", "value1");
        _store.Set("flags/other", "ignored");

        channel.Reader.TryRead(out var evt).Should().BeTrue();
        evt!.Key.Should().Be("config/key1");
        evt.Value.Should().Be("value1");
        evt.Deleted.Should().BeFalse();

        // The flags/ write should not appear
        channel.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Watch_ReceivesDeletes()
    {
        var channel = Channel.CreateUnbounded<KvChangeEvent>();
        _store.Set("config/key1", "value1");

        using var sub = _store.Watch("config/", channel);
        _store.Delete("config/key1");

        channel.Reader.TryRead(out var evt).Should().BeTrue();
        evt!.Key.Should().Be("config/key1");
        evt.Deleted.Should().BeTrue();
    }

    [Fact]
    public void GetAll_ReturnsSnapshot()
    {
        _store.Set("a", "1");
        _store.Set("b", "2");

        var all = _store.GetAll();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void LoadBulk_RestoresState()
    {
        var entries = new[]
        {
            new KvEntry("key1", "value1", 10, DateTimeOffset.UtcNow),
            new KvEntry("key2", "value2", 20, DateTimeOffset.UtcNow)
        };

        _store.LoadBulk(entries);

        _store.Get("key1")!.Value.Should().Be("value1");
        _store.Get("key2")!.Value.Should().Be("value2");
        _store.CurrentVersion.Should().BeGreaterOrEqualTo(20);
    }
}
