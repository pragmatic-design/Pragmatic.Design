using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Client;
using Pragmatic.Agent.Client.Stores;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Agent.Tests.Stores;

/// <summary>
///     When the Agent daemon is unreachable, <see cref="AgentTenantStore"/> degrades to its L0
///     fallback (the previously-registered tenant store) — the same graceful degradation as the config and
///     feature-flag stores.
/// </summary>
public class AgentTenantStoreFallbackTests
{
    [Fact]
    public async Task Disconnected_GetById_ReadsFromFallback()
    {
        var fallback = new FakeTenantStore();
        await fallback.CreateAsync(new TenantInfo
        {
            TenantId = "acme", TenantName = "Acme", State = TenantState.Active, CreatedAt = System.DateTimeOffset.UnixEpoch
        });

        // Unconnected connection → UseFallback is true.
        var store = new AgentTenantStore(new AgentConnection("pragmatic-agent-p7-nonexistent"), fallback);

        var tenant = await store.GetByIdAsync("acme");

        tenant.Should().NotBeNull();
        tenant!.TenantName.Should().Be("Acme");
    }

    [Fact]
    public async Task Disconnected_Create_WritesToFallback()
    {
        var fallback = new FakeTenantStore();
        var store = new AgentTenantStore(new AgentConnection("pragmatic-agent-p7-nonexistent"), fallback);

        await store.CreateAsync(new TenantInfo
        {
            TenantId = "beta", TenantName = "Beta", State = TenantState.Active, CreatedAt = System.DateTimeOffset.UnixEpoch
        });

        (await fallback.GetByIdAsync("beta")).Should().NotBeNull();
    }

    private sealed class FakeTenantStore : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _tenants = new();

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.TryGetValue(tenantId, out var t) ? t : null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _tenants.Values]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => GetAllAsync(ct);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Remove(tenantId));

        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Remove(tenantId));
    }
}
