using Pragmatic.Testing.Assertions;

namespace Pragmatic.Cryptography.EFCore.Tests;

/// <summary>
///     Covers the risk this module was most likely to get wrong: a cached key outliving its destruction
///     and quietly keeping erased data readable.
/// </summary>
/// <remarks>
///     The design answer is structural rather than procedural — the cache holds key material only, and a
///     read checks the subject's status against the store before it resolves anything. These tests exist
///     to prove that property holds, including in the case eviction cannot reach: another process.
/// </remarks>
public sealed class SubjectKeyCacheTests : IDisposable
{
    private readonly SubjectKeyStoreFixture _fixture = new();
    private readonly SubjectKeyCache _cache = new();

    public void Dispose() => _fixture.Dispose();

    private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private SubjectDataProtector CachedProtector()
        => new(new CachingKeyResolver(_fixture.Store, _cache), _fixture.Store);

    [Fact]
    public async Task Read_WithWarmCache_AfterDestruction_StillReportsKeyDestroyed()
    {
        var protector = CachedProtector();
        var packed = await protector.ProtectAsync("subject-a", Bytes("personal"));

        // Warm the cache with a successful read, so the key material is definitely resident.
        (await protector.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.Success);

        await _fixture.Store.DestroyAsync("subject-a");

        var result = await protector.TryReadAsync(packed);

        result.Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
        result.Plain.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_FromAnotherInstanceWhoseCacheWasNeverEvicted_StillReportsKeyDestroyed()
    {
        // The case local eviction cannot reach, and the reason the status is never cached. "Instance B"
        // holds a cache that was warmed before the erasure and is never told about it.
        var instanceB = new SubjectKeyCache();
        var protectorA = CachedProtector();
        var protectorB = new SubjectDataProtector(
            new CachingKeyResolver(_fixture.Store, instanceB), _fixture.Store);

        var packed = await protectorA.ProtectAsync("subject-a", Bytes("personal"));
        (await protectorB.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.Success);

        await _fixture.Store.DestroyAsync("subject-a");

        instanceB.Count.Should().BeGreaterThan(0, "instance B was never told to evict — that is the point");
        (await protectorB.TryReadAsync(packed)).Outcome.Should().Be(DecryptOutcome.KeyDestroyed);
    }

    [Fact]
    public async Task Destroy_EvictsTheKeyFromTheLocalCache()
    {
        var store = new EfCoreSubjectKeyStore(_fixture.Db, _fixture.Master, TimeProvider.System, _cache);
        var resolver = new CachingKeyResolver(store, _cache);

        var key = await store.GetOrCreateAsync("subject-a");
        await resolver.FindByIdAsync(key.KeyId);
        _cache.Count.Should().Be(1);

        await store.DestroyAsync("subject-a");

        _cache.TryGet(key.KeyId, out _).Should().BeFalse("destroyed key material should not linger in memory");
    }

    [Fact]
    public async Task Resolver_SecondLookup_IsServedFromTheCache()
    {
        var spy = new CountingResolver(_fixture.Store);
        var resolver = new CachingKeyResolver(spy, _cache);
        var key = await _fixture.Store.GetOrCreateAsync("subject-a");

        await resolver.FindByIdAsync(key.KeyId);
        await resolver.FindByIdAsync(key.KeyId);

        spy.Calls.Should().Be(1, "the second lookup must not reach the store");
    }

    [Fact]
    public async Task Resolver_UnknownKey_IsNotCached()
    {
        var spy = new CountingResolver(_fixture.Store);
        var resolver = new CachingKeyResolver(spy, _cache);

        await resolver.FindByIdAsync("deadbeef");
        await resolver.FindByIdAsync("deadbeef");

        // Caching a miss would make a key created later invisible until the entry expired.
        spy.Calls.Should().Be(2);
        _cache.Count.Should().Be(0);
    }

    [Fact]
    public void Cache_AtCapacity_StopsAdmittingNewKeys()
    {
        var capped = new SubjectKeyCache { Capacity = 2 };
        var a = EncryptionKey.FromMaterial(new byte[32]);
        var b = EncryptionKey.FromMaterial(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var c = EncryptionKey.FromMaterial(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

        capped.Set(a.KeyId, a);
        capped.Set(b.KeyId, b);
        capped.Set(c.KeyId, c);

        capped.Count.Should().Be(2);
        capped.TryGet(c.KeyId, out _).Should().BeFalse();
    }

    private sealed class CountingResolver(IKeyResolver inner) : IKeyResolver
    {
        public int Calls { get; private set; }

        public ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default)
        {
            Calls++;
            return inner.FindByIdAsync(keyId, ct);
        }
    }
}
