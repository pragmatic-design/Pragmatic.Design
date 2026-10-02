using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Repository;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Repository;

/// <summary>
///     Which tenants a lookup is preloaded for, and what happens when there are none.
/// </summary>
/// <remarks>
///     ⚠️ No database here on purpose: this is the decision the preload makes before it touches one.
///     The defect was that it made no decision at all — a tenant-scoped lookup was loaded at startup
///     under no tenant, against a set whose filter fails closed, so it loaded zero rows and zero rows
///     looks exactly like a lookup table nobody has filled yet.
/// </remarks>
public class LookupPreloadTests
{
    /// <summary>A lookup that is not tenant-scoped is loaded once, under no tenant.</summary>
    /// <remarks>
    ///     The control for everything below: most lookup tables are the same rows for everyone, and
    ///     nothing about them may change.
    /// </remarks>
    [Fact]
    public async Task ASharedLookup_IsLoadedOnceUnderNoTenant()
    {
        var tenants = await LookupPreload
            .TenantsToLoadAsync(new ServiceCollection().BuildServiceProvider(), tenantScoped: false, "Country")
            .ConfigureAwait(true);

        tenants.Should().BeEquivalentTo([(string?)null]);
    }

    /// <summary>A tenant-scoped lookup is loaded once per active tenant.</summary>
    [Fact]
    public async Task ATenantScopedLookup_IsLoadedOncePerActiveTenant()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStore>(new StubTenantStore("tenant-a", "tenant-b"));

        var tenants = await LookupPreload
            .TenantsToLoadAsync(services.BuildServiceProvider(), tenantScoped: true, "StoryTemplate")
            .ConfigureAwait(true);

        tenants.Should().BeEquivalentTo(["tenant-a", "tenant-b"]);
    }

    /// <summary>
    ///     A store that knows no tenants yields no loads — and that is the case the whole issue is
    ///     about, so it is asked directly.
    /// </summary>
    /// <remarks>
    ///     ⚠️ "A store is registered" is not "the store knows the tenants": the default
    ///     <c>InMemoryTenantStore</c> starts empty unless the application seeds it. Returning nothing
    ///     here is correct; being silent about it would be the original defect wearing a new name, so
    ///     the helper logs a warning that says the two apart. The warning is not asserted here — that
    ///     would pin a message rather than a behaviour — and it is named in the issue as the part a
    ///     reader has to look for.
    /// </remarks>
    [Fact]
    public async Task AStoreThatKnowsNoTenants_YieldsNoLoads()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStore>(new StubTenantStore());

        var tenants = await LookupPreload
            .TenantsToLoadAsync(services.BuildServiceProvider(), tenantScoped: true, "StoryTemplate")
            .ConfigureAwait(true);

        tenants.Should().BeEmpty();
    }

    /// <summary>
    ///     A tenant-scoped lookup with no tenant store at all fails at startup rather than loading
    ///     nothing.
    /// </summary>
    /// <remarks>
    ///     The alternative is an application that starts, reports a preload, and throws from every
    ///     navigation over that lookup at request time — a startup misconfiguration disguised as
    ///     scattered runtime failures, which is the shape the hosted service already refuses.
    /// </remarks>
    [Fact]
    public async Task ATenantScopedLookup_WithNoStore_RefusesToStart()
    {
        var act = () => LookupPreload.TenantsToLoadAsync(
            new ServiceCollection().BuildServiceProvider(), tenantScoped: true, "StoryTemplate");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(act).ConfigureAwait(true);

        ex.Message.Should().Contain("StoryTemplate");
        ex.Message.Should().Contain("ITenantStore");
    }

    private sealed class StubTenantStore(params string[] active) : ITenantStore
    {
        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                [.. active.Select(id => new TenantInfo
                {
                    TenantId = id,
                    TenantName = id,
                    State = TenantState.Active,
                    CreatedAt = DateTimeOffset.UnixEpoch
                })]);

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => GetActiveAsync(ct);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(tenant);

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
