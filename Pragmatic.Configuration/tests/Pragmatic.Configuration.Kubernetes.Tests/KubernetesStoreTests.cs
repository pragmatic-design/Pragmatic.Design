using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Configuration.Kubernetes.Tests;

/// <summary>
///     Unit tests for the Kubernetes stores over a fake ConfigMap/Secret seam (a live cluster E2E via k3s is
///     out of scope here). Covers key encoding, per-tenant object isolation, section reads, and idempotence.
/// </summary>
public sealed class KubernetesStoreTests
{
    private sealed class FakeBag : IKubernetesBagApi
    {
        public Dictionary<string, Dictionary<string, string>> Objects { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>?> ReadAsync(string name, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, string>?>(
                Objects.TryGetValue(name, out var data) ? data : null);

        public Task UpsertAsync(string name, IReadOnlyDictionary<string, string> data, CancellationToken ct)
        {
            Objects[name] = new Dictionary<string, string>(data);
            return Task.CompletedTask;
        }
    }

    private static (KubernetesConfigurationStore Store, FakeBag Bag) BuildConfig()
    {
        var bag = new FakeBag();
        var store = new KubernetesConfigurationStore(
            bag, new KubernetesConfigurationOptions { ObjectName = "pragmatic-config", Namespace = "default" });
        return (store, bag);
    }

    [Fact]
    public void Naming_EncodesColonAndReverses()
    {
        KubernetesNaming.DataKey("Booking:Cancel").Should().Be("Booking__Cancel");
        KubernetesNaming.LogicalKey("Booking__Cancel").Should().Be("Booking:Cancel");
        KubernetesNaming.ObjectName("pragmatic-config", "Tenant-A").Should().Be("pragmatic-config-tenant-a");
    }

    [Fact]
    public async Task Set_Get_Delete_RoundTrips()
    {
        var (store, bag) = BuildConfig();

        (await store.GetAsync("Booking:Window")).Should().BeNull();

        await store.SetAsync("Booking:Window", "24");
        (await store.GetAsync("Booking:Window")).Should().Be("24");
        bag.Objects["pragmatic-config"].Should().ContainKey("Booking__Window");

        await store.DeleteAsync("Booking:Window");
        (await store.GetAsync("Booking:Window")).Should().BeNull();
    }

    [Fact]
    public async Task GetSection_ReturnsLogicalKeys()
    {
        var (store, _) = BuildConfig();

        await store.SetAsync("Cache:Ttl", "300");
        await store.SetAsync("Cache:Enabled", "true");
        await store.SetAsync("Other:Key", "x");

        var section = await store.GetSectionAsync("Cache:");

        section.Should().HaveCount(2);
        section["Cache:Ttl"].Should().Be("300");
        section["Cache:Enabled"].Should().Be("true");
    }

    [Fact]
    public async Task Tenant_UsesSeparateObject()
    {
        var (store, bag) = BuildConfig();

        await store.SetAsync("Theme", "default");
        await store.SetAsync("Theme", "dark", "tenant-A");

        (await store.GetAsync("Theme")).Should().Be("default");
        (await store.GetAsync("Theme", "tenant-A")).Should().Be("dark");
        (await store.GetAsync("Theme", "tenant-B")).Should().BeNull();

        bag.Objects.Keys.Should().Contain("pragmatic-config").And.Contain("pragmatic-config-tenant-a");
    }

    [Fact]
    public async Task Delete_MissingKey_IsIdempotent()
    {
        var (store, _) = BuildConfig();
        var act = () => store.DeleteAsync("never");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SecretStore_RoundTrips_OverSameBagLogic()
    {
        var bag = new FakeBag();
        var store = new KubernetesSecretStore(
            bag, new KubernetesConfigurationOptions { ObjectName = "pragmatic-secrets" });
        IWritableSecretStore write = store;

        await write.SetSecretAsync("Db:Password", "s3cret");
        (await store.GetSecretAsync("Db:Password")).Should().Be("s3cret");

        await write.DeleteSecretAsync("Db:Password");
        (await store.GetSecretAsync("Db:Password")).Should().BeNull();
    }
}
