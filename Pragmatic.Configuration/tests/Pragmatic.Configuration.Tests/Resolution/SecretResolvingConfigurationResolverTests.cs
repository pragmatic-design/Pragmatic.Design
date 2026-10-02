using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Providers;
using Pragmatic.Configuration.Resolution;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Configuration.Tests.Resolution;

/// <summary>
///     Verifies <see cref="SecretResolvingConfigurationResolver"/>: a resolved <c>secret://{key}</c>
///     reference is replaced with the secret material from <see cref="ISecretStore"/> at read time, while
///     the literal reference is never surfaced to a consumer.
/// </summary>
public class SecretResolvingConfigurationResolverTests
{
    [Fact]
    public async Task ResolveAsync_SecretReference_ResolvesToSecretValue()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Db:Password", SecretReference.Create("db-password"));
        var secrets = new FakeSecretStore { Global = { ["db-password"] = "s3cr3t" } };

        var resolver = Build(store, secrets);

        (await resolver.ResolveAsync("Db:Password")).Should().Be("s3cr3t");
    }

    [Fact]
    public async Task ResolveAsync_MissingSecret_ReturnsNull_NeverTheLiteralReference()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Db:Password", SecretReference.Create("absent"));
        var resolver = Build(store, new FakeSecretStore());

        var value = await resolver.ResolveAsync("Db:Password");

        value.Should().BeNull();
        value.Should().NotBe(SecretReference.Create("absent"));
    }

    [Fact]
    public async Task ResolveAsync_NonReferenceValue_PassesThroughUnchanged()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Feature:Timeout", "30");
        var resolver = Build(store, new FakeSecretStore());

        (await resolver.ResolveAsync("Feature:Timeout")).Should().Be("30");
    }

    [Fact]
    public async Task ResolveAsync_TenantScopedReference_ResolvesViaTenantSecretFirst()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Api:Key", SecretReference.Create("api-key"));
        var secrets = new FakeSecretStore
        {
            Global = { ["api-key"] = "global-key" },
            Tenant = { [("tenant-a", "api-key")] = "tenant-key" }
        };

        var resolver = Build(store, secrets, new FakeTenantContext("tenant-a"));

        (await resolver.ResolveAsync("Api:Key")).Should().Be("tenant-key");
    }

    [Fact]
    public async Task ResolveWithTrace_SecretReference_KeepsProvenance_SwapsValue()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Db:Password", SecretReference.Create("db-password"));
        var secrets = new FakeSecretStore { Global = { ["db-password"] = "s3cr3t" } };
        var resolver = Build(store, secrets);

        var trace = await resolver.ResolveWithTraceAsync("Db:Password");

        trace.Value.Should().Be("s3cr3t");
        trace.Source.Should().Be(ResolutionSource.Base);
        trace.SourceKey.Should().Be("Db:Password");
    }

    [Fact]
    public async Task ResolveSection_ResolvesReferences_AndDropsMissing()
    {
        var store = new InMemoryConfigurationStore();
        await store.SetAsync("Svc:Plain", "value");
        await store.SetAsync("Svc:Token", SecretReference.Create("token"));
        await store.SetAsync("Svc:Absent", SecretReference.Create("nope"));
        var secrets = new FakeSecretStore { Global = { ["token"] = "resolved-token" } };
        var resolver = Build(store, secrets);

        var section = await resolver.ResolveSectionAsync("Svc:");

        section["Svc:Plain"].Should().Be("value");
        section["Svc:Token"].Should().Be("resolved-token");
        section.Should().NotContainKey("Svc:Absent"); // missing secret → absent, no literal leak
    }

    private static SecretResolvingConfigurationResolver Build(
        IConfigurationStore store, ISecretStore secrets, ITenantContext? tenantContext = null)
    {
        var cascade = new ConfigurationResolver(store, EnvironmentProfile.From("Production"), tenantContext);
        return new SecretResolvingConfigurationResolver(cascade, secrets, tenantContext);
    }

    private sealed class FakeTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => tenantId is not null;
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public Dictionary<string, string> Global { get; } = new();
        public Dictionary<(string Tenant, string Key), string> Tenant { get; } = new();

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Global.TryGetValue(key, out var v) ? v : null);

        public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
            => Task.FromResult(Tenant.TryGetValue((tenantId, key), out var v) ? v : null);
    }
}
