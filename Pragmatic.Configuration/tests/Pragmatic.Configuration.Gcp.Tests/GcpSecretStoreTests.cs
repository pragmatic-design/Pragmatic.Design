using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Gcp.Tests;

/// <summary>
///     Unit tests for the GCP secret store over a fake Secret Manager seam (GCP has no offline emulator, so
///     live E2E is out of scope). Covers id mapping, tenant isolation, and not-found/idempotent semantics.
/// </summary>
public sealed class GcpSecretStoreTests
{
    private sealed class FakeApi : IGcpSecretApi
    {
        public Dictionary<string, string> Store { get; } = new(StringComparer.Ordinal);

        public Task<string?> AccessLatestAsync(string secretId, CancellationToken ct)
            => Task.FromResult(Store.TryGetValue(secretId, out var v) ? v : null);

        public Task UpsertAsync(string secretId, string value, CancellationToken ct)
        {
            Store[secretId] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string secretId, CancellationToken ct)
        {
            Store.Remove(secretId);
            return Task.CompletedTask;
        }
    }

    private static (GcpSecretStore Store, FakeApi Api) Build()
    {
        var api = new FakeApi();
        var store = new GcpSecretStore(api, new GcpConfigurationOptions { ProjectId = "proj", Prefix = "pragmatic" });
        return (store, api);
    }

    [Theory]
    [InlineData(null, "Db:Password", "Db-Password")]
    [InlineData("t1", "Db:Password", "tenants-t1-Db-Password")]
    [InlineData(null, "api/key", "api-key")]
    public void SecretName_MapsToAllowedFlatId(string? tenant, string key, string expectedSuffix)
        => GcpSecretName.Build("pragmatic", tenant, key).Should().Be($"pragmatic-{expectedSuffix}");

    [Fact]
    public async Task Set_Get_Delete_RoundTrips()
    {
        var (store, _) = Build();
        IWritableSecretStore write = store;

        (await store.GetSecretAsync("Db:Password")).Should().BeNull();

        await write.SetSecretAsync("Db:Password", "s3cret");
        (await store.GetSecretAsync("Db:Password")).Should().Be("s3cret");

        await write.DeleteSecretAsync("Db:Password");
        (await store.GetSecretAsync("Db:Password")).Should().BeNull();
    }

    [Fact]
    public async Task TenantSecret_IsIsolatedFromBase()
    {
        var (store, api) = Build();
        IWritableSecretStore write = store;

        await write.SetSecretAsync("api-key", "base");
        await write.SetSecretAsync("api-key", "tenant", "tenant-A");

        (await store.GetSecretAsync("api-key")).Should().Be("base");
        (await store.GetSecretAsync("api-key", "tenant-A")).Should().Be("tenant");
        (await store.GetSecretAsync("api-key", "tenant-B")).Should().BeNull();

        api.Store.Keys.Should().Contain("pragmatic-api-key").And.Contain("pragmatic-tenants-tenant-A-api-key");
    }

    [Fact]
    public async Task Delete_MissingSecret_IsIdempotent()
    {
        var (store, _) = Build();
        var act = () => ((IWritableSecretStore)store).DeleteSecretAsync("never");
        await act.Should().NotThrowAsync();
    }
}
