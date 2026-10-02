using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Tests <see cref="InMemoryTenantStore"/> read/create/update/deactivate/seed operations.
///     The hard-delete path is covered separately in <see cref="InMemoryTenantStoreTests"/>.
/// </summary>
public class InMemoryTenantStoreCrudTests
{
    private static TenantInfo Tenant(string id, TenantState state = TenantState.Active) => new()
    {
        TenantId = id,
        TenantName = $"Tenant {id}",
        State = state,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<InMemoryTenantStore> StoreWith(params TenantInfo[] tenants)
    {
        var store = new InMemoryTenantStore();
        foreach (var t in tenants)
            await store.CreateAsync(t).ConfigureAwait(false);
        return store;
    }

    // ---- CreateAsync -------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NewTenant_ReturnsSameTenant()
    {
        var store = new InMemoryTenantStore();
        var tenant = Tenant("acme");

        var created = await store.CreateAsync(tenant);

        created.Should().BeSameAs(tenant);
        (await store.GetByIdAsync("acme")).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DuplicateTenantId_Throws()
    {
        var store = await StoreWith(Tenant("acme"));

        var act = async () => await store.CreateAsync(Tenant("acme")).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*acme*already exists*").ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateAsync_DuplicateTenantId_IsCaseInsensitive()
    {
        var store = await StoreWith(Tenant("Acme"));

        var act = async () => await store.CreateAsync(Tenant("ACME")).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    // ---- GetByIdAsync ------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingTenant_ReturnsTenant()
    {
        var store = await StoreWith(Tenant("acme"));

        var result = await store.GetByIdAsync("acme");

        result.Should().NotBeNull();
        result!.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownTenant_ReturnsNull()
    {
        var store = await StoreWith(Tenant("acme"));

        var result = await store.GetByIdAsync("ghost");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_IsCaseInsensitive()
    {
        var store = await StoreWith(Tenant("Acme"));

        var result = await store.GetByIdAsync("ACME");

        result.Should().NotBeNull();
        result!.TenantId.Should().Be("Acme");
    }

    // ---- GetAllAsync / GetActiveAsync --------------------------------------

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsEmpty()
    {
        var store = new InMemoryTenantStore();

        var result = await store.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllTenantsRegardlessOfState()
    {
        var store = await StoreWith(Tenant("a"), Tenant("b", TenantState.Deactivated));

        var result = await store.GetAllAsync();

        result.Should().HaveCount(2);
        result.Select(t => t.TenantId).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActiveTenants()
    {
        var store = await StoreWith(
            Tenant("active-1"),
            Tenant("active-2"),
            Tenant("dead", TenantState.Deactivated));

        var result = await store.GetActiveAsync();

        result.Select(t => t.TenantId).Should().BeEquivalentTo("active-1", "active-2");
    }

    [Fact]
    public async Task GetActiveAsync_AllDeactivated_ReturnsEmpty()
    {
        var store = await StoreWith(Tenant("a", TenantState.Deactivated));

        var result = await store.GetActiveAsync();

        result.Should().BeEmpty();
    }

    // ---- UpdateAsync -------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ExistingTenant_ReplacesAndReturnsTrue()
    {
        var store = await StoreWith(Tenant("acme"));
        var updated = Tenant("acme") with { TenantName = "Acme Renamed" };

        var result = await store.UpdateAsync(updated);

        result.Should().BeTrue();
        (await store.GetByIdAsync("acme"))!.TenantName.Should().Be("Acme Renamed");
    }

    [Fact]
    public async Task UpdateAsync_UnknownTenant_ReturnsFalse()
    {
        var store = await StoreWith(Tenant("acme"));

        var result = await store.UpdateAsync(Tenant("ghost"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_IsCaseInsensitive()
    {
        var store = await StoreWith(Tenant("Acme"));
        var updated = Tenant("ACME") with { TenantName = "Updated" };

        var result = await store.UpdateAsync(updated);

        result.Should().BeTrue();
        (await store.GetByIdAsync("acme"))!.TenantName.Should().Be("Updated");
    }

    // ---- DeactivateAsync ---------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_ExistingTenant_SetsStateAndReturnsTrue()
    {
        var store = await StoreWith(Tenant("acme"));

        var result = await store.DeactivateAsync("acme");

        result.Should().BeTrue();
        (await store.GetByIdAsync("acme"))!.State.Should().Be(TenantState.Deactivated);
    }

    [Fact]
    public async Task DeactivateAsync_RemovesTenantFromGetActive()
    {
        var store = await StoreWith(Tenant("acme"), Tenant("other"));

        await store.DeactivateAsync("acme").ConfigureAwait(true);

        (await store.GetActiveAsync()).Should().ContainSingle(t => t.TenantId == "other");
    }

    [Fact]
    public async Task DeactivateAsync_UnknownTenant_ReturnsFalse()
    {
        var store = await StoreWith(Tenant("acme"));

        var result = await store.DeactivateAsync("ghost");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_IsCaseInsensitive()
    {
        var store = await StoreWith(Tenant("Acme"));

        var result = await store.DeactivateAsync("ACME");

        result.Should().BeTrue();
        (await store.GetByIdAsync("acme"))!.State.Should().Be(TenantState.Deactivated);
    }

    // ---- Seed --------------------------------------------------------------

    [Fact]
    public async Task Seed_AddsTenantsThatAreRetrievable()
    {
        var store = new InMemoryTenantStore();

        store.Seed([Tenant("a"), Tenant("b")]);

        (await store.GetAllAsync()).Select(t => t.TenantId).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task Seed_ExistingTenantId_OverwritesWithoutThrowing()
    {
        var store = await StoreWith(Tenant("acme"));

        store.Seed([Tenant("acme") with { TenantName = "Seeded Over" }]);

        (await store.GetByIdAsync("acme"))!.TenantName.Should().Be("Seeded Over");
    }

    [Fact]
    public void Seed_EmptySequence_DoesNothing()
    {
        var store = new InMemoryTenantStore();

        var act = () => store.Seed([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ImplementsITenantStore()
    {
        ITenantStore store = new InMemoryTenantStore();
        store.Should().NotBeNull();
    }
}
