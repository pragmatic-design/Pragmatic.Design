using Pragmatic.Testing.Assertions;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Tests <see cref="InMemoryTenantStore"/>, focusing on the hard-delete removal path
///     (<see cref="InMemoryTenantStore.DeleteAsync"/>) added alongside the tenant lifecycle API.
/// </summary>
public class InMemoryTenantStoreTests
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

    [Fact]
    public async Task DeleteAsync_ExistingTenant_RemovesAndReturnsTrue()
    {
        var store = await StoreWith(Tenant("acme"));

        var deleted = await store.DeleteAsync("acme");

        deleted.Should().BeTrue();
        (await store.GetByIdAsync("acme")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownTenant_ReturnsFalse()
    {
        var store = await StoreWith(Tenant("acme"));

        var deleted = await store.DeleteAsync("ghost");

        deleted.Should().BeFalse();
        (await store.GetByIdAsync("acme")).Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_IsCaseInsensitive()
    {
        var store = await StoreWith(Tenant("Acme"));

        var deleted = await store.DeleteAsync("ACME");

        deleted.Should().BeTrue();
        (await store.GetByIdAsync("acme")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromGetAllAndGetActive()
    {
        var store = await StoreWith(Tenant("a"), Tenant("b"));

        await store.DeleteAsync("a").ConfigureAwait(true);

        (await store.GetAllAsync()).Should().ContainSingle(t => t.TenantId == "b");
        (await store.GetActiveAsync()).Should().OnlyContain(t => t.TenantId == "b");
    }

    [Fact]
    public async Task DeleteAsync_Twice_SecondReturnsFalse()
    {
        var store = await StoreWith(Tenant("acme"));

        (await store.DeleteAsync("acme")).Should().BeTrue();
        (await store.DeleteAsync("acme")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_AfterDelete_Succeeds()
    {
        var store = await StoreWith(Tenant("acme"));
        await store.DeleteAsync("acme").ConfigureAwait(true);

        var act = async () => await store.CreateAsync(Tenant("acme")).ConfigureAwait(false);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
        (await store.GetByIdAsync("acme")).Should().NotBeNull();
    }
}
