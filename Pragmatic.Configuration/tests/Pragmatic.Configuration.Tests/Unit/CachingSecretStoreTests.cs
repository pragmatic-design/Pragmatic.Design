using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Pragmatic.Configuration.Cache;
using Xunit;

namespace Pragmatic.Configuration.Tests.Unit;

public class CachingSecretStoreTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Read_IsServedFromCache_AfterFirstFetch()
    {
        var inner = new FakeSecretStore();
        inner.Seed("db-password", "s3cret");
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(inner, cache, Ttl);

        var first = await store.GetSecretAsync("db-password");
        var second = await store.GetSecretAsync("db-password");

        first.Should().Be("s3cret");
        second.Should().Be("s3cret");
        inner.Fetches.Should().Be(1, "the second read must be served from cache");
    }

    [Fact]
    public async Task Write_InvalidatesCache()
    {
        var inner = new FakeSecretStore();
        inner.Seed("api-key", "old");
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(inner, cache, Ttl);

        (await store.GetSecretAsync("api-key")).Should().Be("old"); // warms cache
        await store.SetSecretAsync("api-key", "new");

        (await store.GetSecretAsync("api-key")).Should().Be("new", "a write must invalidate the cached value");
    }

    [Fact]
    public async Task Delete_InvalidatesCache()
    {
        var inner = new FakeSecretStore();
        inner.Seed("token", "value");
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(inner, cache, Ttl);

        (await store.GetSecretAsync("token")).Should().Be("value");
        await store.DeleteSecretAsync("token");

        (await store.GetSecretAsync("token")).Should().BeNull();
    }

    [Fact]
    public async Task AlreadyExpiredSecret_IsNeverCached()
    {
        // Rotation-aware TTL: a value whose advertised expiry is already in the past must not be cached,
        // so every read re-fetches (and picks up a rotation immediately).
        var inner = new FakeSecretStore();
        inner.Seed("rotating", "v1", expires: DateTimeOffset.UtcNow.AddSeconds(-1));
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(inner, cache, Ttl);

        await store.GetSecretAsync("rotating");
        await store.GetSecretAsync("rotating");

        inner.Fetches.Should().Be(2, "an already-expired secret must not be served from cache");
    }

    [Fact]
    public async Task Metadata_IsPreserved_ThroughCache()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var inner = new FakeSecretStore();
        inner.Seed("k", "v", expires: expiresAt);
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(inner, cache, Ttl);

        var entry = await store.GetSecretWithMetadataAsync("k");

        entry.Value.Should().Be("v");
        entry.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task Write_OnReadOnlyInner_Throws()
    {
        using var cache = new InMemoryConfigurationCacheStack();
        var store = new CachingSecretStore(new ReadOnlySecretStore(), cache, Ttl);

        var act = () => store.SetSecretAsync("k", "v");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    private sealed class FakeSecretStore : ISecretStore, IWritableSecretStore
    {
        private readonly Dictionary<string, (string Value, DateTimeOffset? Expires)> _store = new();

        public int Fetches { get; private set; }

        public void Seed(string key, string value, DateTimeOffset? expires = null) => _store[key] = (value, expires);

        public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => (await GetSecretWithMetadataAsync(key, ct)).Value;

        public async Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
            => (await GetSecretWithMetadataAsync(key, tenantId, ct)).Value;

        public Task<SecretEntry> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
        {
            Fetches++;
            return Task.FromResult(_store.TryGetValue(key, out var e) ? new SecretEntry(e.Value, e.Expires) : SecretEntry.NotFound);
        }

        public Task<SecretEntry> GetSecretWithMetadataAsync(string key, string tenantId, CancellationToken ct = default)
            => GetSecretWithMetadataAsync(key, ct);

        public Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        {
            _store[key] = (value, null);
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ReadOnlySecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
}
