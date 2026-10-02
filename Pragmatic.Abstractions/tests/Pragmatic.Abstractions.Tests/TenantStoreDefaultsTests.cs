using Pragmatic.Testing.Assertions;
using Pragmatic.MultiTenancy;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class TenantStoreDefaultsTests
{
    // Minimal stub overriding ONLY the abstract members so the DeleteAsync default
    // interface method (which throws NotSupportedException) is exercised.
    private sealed class StubTenantStore : ITenantStore
    {
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([]);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(tenant);

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task DeleteAsync_DefaultThrowsNotSupported()
    {
        ITenantStore store = new StubTenantStore();

        var act = () => store.DeleteAsync("tenant-1");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
